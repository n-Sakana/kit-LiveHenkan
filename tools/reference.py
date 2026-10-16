import pathlib,json,subprocess,time,urllib.request,unicodedata,socket,os,argparse
root=pathlib.Path(__file__).resolve().parent.parent
def resolve_model_dir(model_dir=''):
 return pathlib.Path(model_dir or os.environ.get('JINEN_MODEL_DIR') or root).resolve()
def prompt(s):return unicodedata.normalize('NFKC','\uee02\uee00'+''.join(chr(ord(c)+96) if '\u3041'<=c<='\u3096' else c for c in s)+'\uee01')
def run(size,data):
 from tokenizers import Tokenizer
 tok=Tokenizer.from_file(str(data/f'models/tokenizer-{size}.json'));tok.no_padding();tok.no_truncation()
 corpus=json.loads(pathlib.Path('fixtures/corpus36.json').read_text(encoding='utf8'))
 with socket.socket() as sk:sk.bind(('127.0.0.1',0));port=sk.getsockname()[1]
 log=open(f'results/llama-{size}.log','w',encoding='utf8')
 cmd=[str(data/'reference/llama/llama-server.exe'),'-m',str(data/f'models/jinen-v2-{size}-Q5_K_M.gguf'),'--offline','--host','127.0.0.1','--port',str(port),'-t','4','-tb','4','-c','1024','-np','1','-ngl','0','-fa','off','-ctk','f32','-ctv','f32','--no-warmup']
 started=time.perf_counter();p=subprocess.Popen(cmd,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
 lifecycle={'pid':p.pid,'command':cmd};print('started',p.pid,size,flush=True)
 try:
  for attempt in range(120):
   if p.poll() is not None:raise RuntimeError('server exited '+str(p.returncode))
   try:
    with urllib.request.urlopen(f'http://127.0.0.1:{port}/health',timeout=1) as res:
     if res.status==200:break
   except Exception:time.sleep(.25)
  else:raise TimeoutError('server not healthy')
  lifecycle['ready_ms']=(time.perf_counter()-started)*1000
  results=[]
  for row in corpus:
   ids=tok.encode(prompt(row['reading']),add_special_tokens=False).ids
   body=dict(prompt=ids,n_predict=50,temperature=0,seed=1,cache_prompt=False,return_tokens=True,repeat_penalty=1.0)
   req=urllib.request.Request(f'http://127.0.0.1:{port}/completion',data=json.dumps(body).encode(),headers={'Content-Type':'application/json'})
   t=time.perf_counter()
   with urllib.request.urlopen(req,timeout=60) as res:out=json.load(res)
   tokens=out.get('tokens');clean=[i for i in tokens if i>=4 and i<32000] if tokens is not None else None
   text=tok.decode(clean,skip_special_tokens=False).strip() if clean is not None else out['content'].strip()
   result=dict(row,PromptTokens=ids,OutputTokens=tokens,Text=text,WallMs=(time.perf_counter()-t)*1000,Response=out);results.append(result)
   print(size,row['id'],text,flush=True)
  pathlib.Path(f'results/reference-{size}.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf8')
 finally:
  if p.poll() is None:p.terminate();p.wait(timeout=15)
  lifecycle['exit_code']=p.returncode;lifecycle['confirmed_exited']=p.poll() is not None
  pathlib.Path(f'results/reference-{size}-process.json').write_text(json.dumps(lifecycle,indent=2),encoding='utf8');log.close();print('terminated and waited pid',p.pid,flush=True)
def main(argv=None):
 parser=argparse.ArgumentParser(description='Run reference conversion with local models.')
 parser.add_argument('-ModelDir','--model-dir',default='',help='Model directory; defaults to JINEN_MODEL_DIR, then the project folder.')
 parser.add_argument('sizes',nargs='*',help='Model sizes to run (default: xsmall small).')
 args=parser.parse_args(argv)
 data=resolve_model_dir(args.model_dir)
 os.chdir(root)
 (root/'results').mkdir(exist_ok=True)
 for size in args.sizes or ['xsmall','small']:run(size,data)
if __name__=='__main__':main()
