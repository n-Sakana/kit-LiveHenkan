"""Preparation only: no Python or numpy is needed on the destination."""
import struct,json,pathlib,numpy as np,hashlib
class GGUF:
 def __init__(self,path):
  self.f=open(path,'rb');assert self.f.read(4)==b'GGUF';assert self.u('I')==3
  nt=self.u('Q');nk=self.u('Q');self.meta={}
  for _ in range(nk):
   k=self.s();self.meta[k]=self.v(self.u('I'))
  self.ts=[]
  for _ in range(nt):
   name=self.s();dims=[self.u('Q') for _ in range(self.u('I'))];typ=self.u('I');off=self.u('Q');self.ts.append((name,dims,typ,off))
  align=self.meta.get('general.alignment',32);self.base=(self.f.tell()+align-1)//align*align
 def u(self,fmt):return struct.unpack('<'+fmt,self.f.read(struct.calcsize('<'+fmt)))[0]
 def s(self):return self.f.read(self.u('Q')).decode()
 def v(self,t):
  if t==8:return self.s()
  if t==9:
   et=self.u('I');return [self.v(et) for _ in range(self.u('Q'))]
  return self.u({0:'B',1:'b',2:'H',3:'h',4:'I',5:'i',6:'f',7:'?',10:'Q',11:'q',12:'d'}[t])
 def tensor(self,t):
  name,dims,typ,off=t;n=int(np.prod(dims));self.f.seek(self.base+off)
  if typ==0:return np.frombuffer(self.f.read(n*4),dtype='<f4')
  sizes={7:(32,24),8:(32,34),13:(256,176),14:(256,210)};bs,sz=sizes[typ]
  b=np.frombuffer(self.f.read(n//bs*sz),dtype=np.uint8).reshape(-1,sz);y=np.empty((len(b),bs),np.float32)
  def half(i):return b[:,i:i+2].copy().view('<f2').reshape(-1,1).astype(np.float32)
  if typ==7:
   d,m=half(0),half(2);qh=b[:,4:8].copy().view('<u4').reshape(-1,1);qs=b[:,8:24]
   for j in range(32):y[:,j]=(d[:,0]*((qs[:,j%16]>>(0 if j<16 else 4)&15)+((qh[:,0]>>j&1)*16))+m[:,0])
  elif typ==8:y=half(0)*b[:,2:].copy().view(np.int8).astype(np.float32)
  elif typ==13:
   d,m=half(0),half(2);sc=b[:,4:16].astype(np.int32);qh=b[:,16:48];qs=b[:,48:]
   for j in range(8):
    if j<4:s=sc[:,j]&63;mn=sc[:,j+4]&63
    else:s=(sc[:,j+4]&15)|((sc[:,j-4]>>6)<<4);mn=(sc[:,j+4]>>4)|((sc[:,j]>>6)<<4)
    q=(qs[:,(j//2)*32:(j//2+1)*32]>>(4*(j%2))&15)+((qh>>j&1)*16)
    y[:,j*32:(j+1)*32]=(d*s[:,None])*q-(m*mn[:,None])
  elif typ==14:
   d=half(208);sc=b[:,192:208].copy().view(np.int8).astype(np.float32)
   for h in range(2):
    ql=b[:,h*64:h*64+64];qh=b[:,128+h*32:160+h*32]
    for j in range(4):
     q=((ql[:,(j%2)*32:(j%2+1)*32]>>(4*(j//2))&15)|((qh>>(2*j)&3)<<4)).astype(np.int32)-32
     scale=np.repeat(sc[:,h*8+j*2:h*8+j*2+2],16,axis=1)
     y[:,h*128+j*32:h*128+(j+1)*32]=(d*scale)*q
  return y.reshape(-1).astype('<f4')
def convert(gguf,tokenizer,out,metadata):
 g=GGUF(gguf);m=g.meta
 tok=json.load(open(tokenizer,encoding='utf8'))
 out=pathlib.Path(out);out.parent.mkdir(parents=True,exist_ok=True);f=out.open('xb')
 def i(v):f.write(struct.pack('<i',v))
 def s(v):b=v.encode();i(len(b));f.write(b)
 f.write(b'JINEN001')
 for k in ['block_count','embedding_length','feed_forward_length','attention.head_count','attention.head_count_kv','attention.key_length']:i(m['qwen3.'+k])
 f.write(struct.pack('<ff',m['qwen3.attention.layer_norm_rms_epsilon'],m['qwen3.rope.freq_base']))
 vocab=tok['model']['vocab'];i(len(vocab));inv={v:k for k,v in vocab.items()}
 for j in range(len(vocab)):s(inv[j])
 merges=tok['model']['merges'];i(len(merges))
 for a,b in merges:i(vocab[a]);i(vocab[b]);i(vocab[a+b])
 i(len(g.ts))
 for t in g.ts:
  name,dims,typ,off=t;s(name);i(dims[0]);i(int(np.prod(dims))//dims[0]);f.write(g.tensor(t).tobytes())
 f.close()
 info={k:v for k,v in m.items() if not isinstance(v,list)};info['tensors']=[dict(name=n,dims=d,type=t) for n,d,t,o in g.ts];info['gguf_bytes']=pathlib.Path(g.f.name).stat().st_size;info['portable_bytes']=out.stat().st_size;info['portable_sha256']=hashlib.sha256(out.read_bytes()).hexdigest()
 pathlib.Path(metadata).write_text(json.dumps(info,ensure_ascii=False,indent=2),encoding='utf8');print(out,info['gguf_bytes'],info['portable_bytes'],sorted(set(t[2] for t in g.ts)),flush=True)
if __name__=='__main__':
 import argparse
 p=argparse.ArgumentParser(description=__doc__)
 p.add_argument('--gguf',required=True);p.add_argument('--tokenizer',required=True);p.add_argument('--output',required=True);p.add_argument('--metadata')
 a=p.parse_args();convert(a.gguf,a.tokenizer,a.output,a.metadata or a.output+'.metadata.json')
