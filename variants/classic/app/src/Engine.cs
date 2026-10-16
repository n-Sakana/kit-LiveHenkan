// Pure managed Qwen3 inference for Windows PowerShell 5.1 / .NET Framework 4.8.
// No native library, network or external process. System.Numerics.Vector4 (in-box) is the only SIMD.
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Jinen {
 public sealed class Result {
  public string Reading,Context,Text,StopReason;
  public int PromptTokens,OutputTokens,ReusedTokens;
  public double Milliseconds,PrefillMilliseconds;
  public float LogProb;
 }
 public sealed class Candidate { public string Text; public float LogProb; }

 // Work is cut into more pieces than threads and every thread, the caller included,
 // takes the next piece. A descheduled worker therefore never stalls a conversion.
 sealed class Pool : IDisposable {
  readonly Thread[] threads; readonly ManualResetEventSlim wake=new ManualResetEventSlim(false,0);
  Action<int> body; int pieces,left; long state; int jobId; volatile bool stop;
  public readonly int Threads;
  public Pool(int count){
   Threads=Math.Max(1,count);threads=new Thread[Threads-1];
   for(int i=0;i<threads.Length;i++){threads[i]=new Thread(Loop);threads[i].IsBackground=true;threads[i].Name="jinen-"+i;threads[i].Start();}
  }
  // The spin loops below hand the processor back every few turns. Thread.SpinWait alone never lets the garbage collector
  // stop the thread, and a collection that cannot stop one thread keeps every other thread of the process frozen:
  // the keyboard hook then misses the time Windows allows it and keys reach the application unconverted.
  void Loop(){
   int seen=0,turns=0;
   while(!stop){
    if(!wake.IsSet){wake.Wait(200);continue;}
    int j=(int)(Volatile.Read(ref state)>>32);
    if(j==seen){Thread.SpinWait(24);if((++turns&7)==0)Thread.Yield();continue;}
    seen=j;Drain(j);
   }
  }
  // A piece is claimed together with the job number, so a thread that is late
  // leaving the previous job can never take a piece of the next one by mistake.
  void Drain(int j){
   while(true){
    long cur=Volatile.Read(ref state);
    if((int)(cur>>32)!=j)return;
    int i=(int)cur;
    if(i>=pieces)return;
    if(Interlocked.CompareExchange(ref state,cur+1,cur)!=cur)continue;
    body(i);Interlocked.Decrement(ref left);
   }
  }
  // Workers spin only between Begin and End, so an idle engine uses no CPU.
  public void Begin(){if(threads.Length>0)wake.Set();}
  public void End(){if(threads.Length>0)wake.Reset();}
  public void Run(Action<int> work,int count){
   if(threads.Length==0||count<=1){for(int i=0;i<count;i++)work(i);return;}
   body=work;pieces=count;Volatile.Write(ref left,count);
   int j=++jobId;if(j==int.MaxValue)jobId=0;
   Volatile.Write(ref state,(long)j<<32);
   Drain(j);
   int turns=0;
   while(Volatile.Read(ref left)>0){Thread.SpinWait(8);if((++turns&7)==0)Thread.Yield();}
  }
  public void Dispose(){stop=true;wake.Set();}
 }

 public sealed unsafe class Engine : IDisposable {
  public const int Context=512,Eos=3,MaxNewTokens=64;
  public int Layers,Dim,Hidden,Heads,KVHeads,HeadDim,Threads,VocabRows;
  public double LoadMilliseconds; public long WeightBytes;
  public volatile bool Cancel;
  public static int PieceFactor=4;
  float eps,ropeBase; string[] vocab;
  readonly Dictionary<string,int> ids=new Dictionary<string,int>(StringComparer.Ordinal);
  readonly Dictionary<long,int[]> merges=new Dictionary<long,int[]>();
  readonly List<IntPtr> blocks=new List<IntPtr>();
  sealed class Layer {public float* AttnNorm,Q,K,V,O,QNorm,KNorm,FfnNorm,Gate,Up,Down;}
  Layer[] layers; float* embed,outputW,outputNorm;
  float* kc,vc,x,z,q,k,v,att,o,gate,up,logits,scores,ropeCos,ropeSin;
  readonly Pool pool;
  int[] cached=new int[Context]; int cachedCount;
  // Two key/value caches: typing keeps slot 0 warm while candidate searches use slot 1.
  readonly float*[] kcSlots=new float*[2],vcSlots=new float*[2]; readonly int[][] cachedSlots=new int[2][]; readonly int[] countSlots=new int[2]; int slot;
  // Current parallel job. One job runs at a time, so plain fields are enough.
  float* jw0,jw1,jw2,jx,jy0,jy1,jy2; int jCols,jRows,jStep; bool jSilu;
  readonly Action<int> matJob;

  float* Alloc(long floats){IntPtr p=Marshal.AllocHGlobal(new IntPtr(floats*4));blocks.Add(p);return (float*)p;}
  static string ReadString(BinaryReader r){int n=r.ReadInt32();return Encoding.UTF8.GetString(r.ReadBytes(n));}
  static long Key(int a,int b){return ((long)a<<32)|(uint)b;}

  public Engine(string file,int threads){
   Stopwatch sw=Stopwatch.StartNew();
   Dictionary<string,IntPtr> tensors=new Dictionary<string,IntPtr>(StringComparer.Ordinal);
   Dictionary<string,int> rows=new Dictionary<string,int>(StringComparer.Ordinal);
   using(FileStream fs=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,1<<20,FileOptions.SequentialScan))
   using(BinaryReader r=new BinaryReader(fs)){
    if(Encoding.ASCII.GetString(r.ReadBytes(8))!="JINEN001")throw new InvalidDataException("Bad model format");
    Layers=r.ReadInt32();Dim=r.ReadInt32();Hidden=r.ReadInt32();Heads=r.ReadInt32();KVHeads=r.ReadInt32();HeadDim=r.ReadInt32();eps=r.ReadSingle();ropeBase=r.ReadSingle();
    if(Heads!=KVHeads||Heads*HeadDim!=Dim)throw new InvalidDataException("Unsupported attention layout");
    vocab=new string[r.ReadInt32()];for(int i=0;i<vocab.Length;i++){vocab[i]=ReadString(r);ids[vocab[i]]=i;}
    int nm=r.ReadInt32();for(int i=0;i<nm;i++){int a=r.ReadInt32(),b=r.ReadInt32(),c=r.ReadInt32();merges[Key(a,b)]=new int[]{i,c};}
    int nt=r.ReadInt32();byte[] buf=new byte[1<<20];
    for(int i=0;i<nt;i++){
     string name=ReadString(r);int cols=r.ReadInt32(),rws=r.ReadInt32();long bytes=(long)cols*rws*4;WeightBytes+=bytes;
     float* dest=Alloc((long)cols*rws);byte* d=(byte*)dest;
     for(long p=0;p<bytes;){int n=r.Read(buf,0,(int)Math.Min(buf.Length,bytes-p));if(n==0)throw new EndOfStreamException();Marshal.Copy(buf,0,new IntPtr(d+p),n);p+=n;}
     tensors.Add(name,new IntPtr(dest));rows[name]=rws;
    }
    if(fs.Position!=fs.Length)throw new InvalidDataException("Trailing model bytes");
   }
   embed=(float*)tensors["token_embd.weight"];VocabRows=rows["token_embd.weight"];
   outputW=tensors.ContainsKey("output.weight")?(float*)tensors["output.weight"]:embed;
   outputNorm=(float*)tensors["output_norm.weight"];
   layers=new Layer[Layers];
   for(int l=0;l<Layers;l++){
    string p="blk."+l+".";
    layers[l]=new Layer{AttnNorm=(float*)tensors[p+"attn_norm.weight"],Q=(float*)tensors[p+"attn_q.weight"],K=(float*)tensors[p+"attn_k.weight"],V=(float*)tensors[p+"attn_v.weight"],O=(float*)tensors[p+"attn_output.weight"],QNorm=(float*)tensors[p+"attn_q_norm.weight"],KNorm=(float*)tensors[p+"attn_k_norm.weight"],FfnNorm=(float*)tensors[p+"ffn_norm.weight"],Gate=(float*)tensors[p+"ffn_gate.weight"],Up=(float*)tensors[p+"ffn_up.weight"],Down=(float*)tensors[p+"ffn_down.weight"]};
   }
   for(int i=0;i<2;i++){kcSlots[i]=Alloc((long)Layers*Context*Dim);vcSlots[i]=Alloc((long)Layers*Context*Dim);cachedSlots[i]=new int[Context];}
   kc=kcSlots[0];vc=vcSlots[0];cached=cachedSlots[0];
   x=Alloc(Dim);z=Alloc(Dim);q=Alloc(Dim);k=Alloc(Dim);v=Alloc(Dim);att=Alloc(Dim);o=Alloc(Math.Max(Dim,Hidden));gate=Alloc(Hidden);up=Alloc(Hidden);logits=Alloc(VocabRows);scores=Alloc(Context);
   int half=HeadDim/2;ropeCos=Alloc((long)Context*half);ropeSin=Alloc((long)Context*half);
   for(int pos=0;pos<Context;pos++)for(int j=0;j<half;j++){double angle=pos/Math.Pow(ropeBase,2.0*j/HeadDim);ropeCos[pos*half+j]=(float)Math.Cos(angle);ropeSin[pos*half+j]=(float)Math.Sin(angle);}
   Threads=Math.Max(1,threads);pool=new Pool(Threads);matJob=MatPiece;
   sw.Stop();LoadMilliseconds=sw.Elapsed.TotalMilliseconds;
  }
  public void Dispose(){pool.Dispose();foreach(IntPtr p in blocks)Marshal.FreeHGlobal(p);blocks.Clear();}

  // ---- tokenizer -------------------------------------------------------
  public static string Katakana(string reading){
   StringBuilder b=new StringBuilder(reading.Length);
   foreach(char c in reading)b.Append(c>='ぁ'&&c<='ゖ'?(char)(c+0x60):c);
   return b.ToString();
  }
  public static string Prompt(string context,string reading){
   return (""+(context??"")+""+Katakana(reading)+"").Normalize(NormalizationForm.FormKC);
  }
  void Bpe(string chunk,List<int> dest){
   if(chunk.Length==0)return;List<int> t=new List<int>();
   for(int p=0;p<chunk.Length;p++){
    string s=chunk[p].ToString();if(char.IsHighSurrogate(chunk[p])&&p+1<chunk.Length&&char.IsLowSurrogate(chunk[p+1]))s+=chunk[++p];
    int id;if(ids.TryGetValue(s,out id))t.Add(id);else foreach(byte b in Encoding.UTF8.GetBytes(s))t.Add(ids["<0x"+b.ToString("X2")+">"]);
   }
   while(t.Count>1){
    int best=int.MaxValue,pos=-1,val=0;
    for(int i=0;i<t.Count-1;i++){int[] m;if(merges.TryGetValue(Key(t[i],t[i+1]),out m)&&m[0]<best){best=m[0];pos=i;val=m[1];}}
    if(pos<0)break;t[pos]=val;t.RemoveAt(pos+1);
   }
   dest.AddRange(t);
  }
  public int[] Tokenize(string text){
   text=text.Normalize(NormalizationForm.FormKC);List<int> res=new List<int>();StringBuilder chunk=new StringBuilder();
   for(int p=0;p<text.Length;p++){
    int special=-1,used=1;char c=text[p];
    if(c>=''&&c<='')special=32000+c-'';
    else if(c=='<'){int end=text.IndexOf('>',p);if(end>=p&&end-p<=6){string s=text.Substring(p,end-p+1);int id;if(ids.TryGetValue(s,out id)&&id<260){special=id;used=s.Length;}}}
    if(special>=0||char.IsDigit(c)){Bpe(chunk.ToString(),res);chunk.Length=0;if(special>=0){res.Add(special);p+=used-1;}else Bpe(c.ToString(),res);}
    else chunk.Append(c);
   }
   Bpe(chunk.ToString(),res);return res.ToArray();
  }
  public string Decode(IList<int> tokens){
   List<byte> bytes=new List<byte>();
   foreach(int id in tokens){
    if(id<0||id>=vocab.Length)continue;if(id<4||id>=32000)continue;
    string s=vocab[id];
    if(s.Length==6&&s.StartsWith("<0x",StringComparison.Ordinal)&&s[5]=='>')bytes.Add(System.Convert.ToByte(s.Substring(3,2),16));else bytes.AddRange(Encoding.UTF8.GetBytes(s));
   }
   return Encoding.UTF8.GetString(bytes.ToArray()).Trim();
  }

  // ---- kernels ---------------------------------------------------------
  static float Dot(float* a,float* b,int n){
   Vector4 s0=Vector4.Zero,s1=Vector4.Zero,s2=Vector4.Zero,s3=Vector4.Zero;int j=0;
   for(;j+15<n;j+=16){s0+=*(Vector4*)(a+j)* *(Vector4*)(b+j);s1+=*(Vector4*)(a+j+4)* *(Vector4*)(b+j+4);s2+=*(Vector4*)(a+j+8)* *(Vector4*)(b+j+8);s3+=*(Vector4*)(a+j+12)* *(Vector4*)(b+j+12);}
   Vector4 t=(s0+s1)+(s2+s3);float sum=t.X+t.Y+t.Z+t.W;
   for(;j<n;j++)sum+=a[j]*b[j];
   return sum;
  }
  void MatPiece(int piece){
   int from=piece*jStep,to=Math.Min(jRows,from+jStep),n=jCols;float* xp=jx;
   for(int r=from;r<to;r++)jy0[r]=Dot(jw0+(long)r*n,xp,n);
   if(jw1!=null)for(int r=from;r<to;r++)jy1[r]=Dot(jw1+(long)r*n,xp,n);
   if(jw2!=null)for(int r=from;r<to;r++)jy2[r]=Dot(jw2+(long)r*n,xp,n);
   if(jSilu)for(int r=from;r<to;r++)jy0[r]=(float)(jy0[r]/(1.0+Math.Exp(-jy0[r])))*jy1[r];
  }
  // Up to three matrices that share one input are multiplied in a single dispatch.
  void Mat(float* w0,float* y0,float* w1,float* y1,float* w2,float* y2,float* input,int cols,int rws,bool silu){
   jw0=w0;jy0=y0;jw1=w1;jy1=y1;jw2=w2;jy2=y2;jx=input;jCols=cols;jRows=rws;jSilu=silu;
   int pieces=Threads==1?1:Math.Min(Threads*PieceFactor,Math.Max(1,rws/16));
   jStep=(rws+pieces-1)/pieces;
   pool.Run(matJob,pieces);
  }
  void Norm(float* src,float* dest,float* w,int total,int width){
   for(int p=0;p<total;p+=width){
    double ss=0;for(int j=0;j<width;j++)ss+=(double)src[p+j]*src[p+j];
    float s=(float)(1.0/Math.Sqrt(ss/width+eps));
    for(int j=0;j<width;j++)dest[p+j]=src[p+j]*s*w[j];
   }
  }
  void Rope(float* t,int pos){
   int half=HeadDim/2;float* c=ropeCos+pos*half,s=ropeSin+pos*half;
   for(int h=0;h<Dim;h+=HeadDim)for(int j=0;j<half;j++){float a=t[h+j],b=t[h+j+half];t[h+j]=a*c[j]-b*s[j];t[h+j+half]=a*s[j]+b*c[j];}
  }
  void Step(int token,int pos,bool wantLogits){
   Buffer.MemoryCopy(embed+(long)token*Dim,x,Dim*4,Dim*4);
   float scale=(float)(1.0/Math.Sqrt(HeadDim));
   for(int l=0;l<Layers;l++){
    Layer L=layers[l];float* kl=kc+(long)l*Context*Dim,vl=vc+(long)l*Context*Dim;
    Norm(x,z,L.AttnNorm,Dim,Dim);
    Mat(L.Q,q,L.K,k,L.V,v,z,Dim,Dim,false);
    Norm(q,q,L.QNorm,Dim,HeadDim);Norm(k,k,L.KNorm,Dim,HeadDim);
    Rope(q,pos);Rope(k,pos);
    Buffer.MemoryCopy(k,kl+(long)pos*Dim,Dim*4,Dim*4);Buffer.MemoryCopy(v,vl+(long)pos*Dim,Dim*4,Dim*4);
    for(int j=0;j<Dim;j++)att[j]=0;
    for(int h=0;h<Heads;h++){
     int off=h*HeadDim;float max=float.NegativeInfinity;
     for(int t=0;t<=pos;t++){float sc=Dot(q+off,kl+(long)t*Dim+off,HeadDim)*scale;scores[t]=sc;if(sc>max)max=sc;}
     double denom=0;for(int t=0;t<=pos;t++){scores[t]=(float)Math.Exp(scores[t]-max);denom+=scores[t];}
     for(int t=0;t<=pos;t++){
      Vector4 a=new Vector4((float)(scores[t]/denom));float* src=vl+(long)t*Dim+off,dst=att+off;
      for(int j=0;j<HeadDim;j+=4)*(Vector4*)(dst+j)+=a* *(Vector4*)(src+j);
     }
    }
    Mat(L.O,o,null,null,null,null,att,Dim,Dim,false);
    for(int j=0;j<Dim;j++)x[j]+=o[j];
    Norm(x,z,L.FfnNorm,Dim,Dim);
    Mat(L.Gate,gate,L.Up,up,null,null,z,Dim,Hidden,true);
    Mat(L.Down,o,null,null,null,null,gate,Hidden,Dim,false);
    for(int j=0;j<Dim;j++)x[j]+=o[j];
   }
   cached[pos]=token;cachedCount=pos+1;
   if(!wantLogits)return;
   Norm(x,z,outputNorm,Dim,Dim);
   Mat(outputW,logits,null,null,null,null,z,Dim,VocabRows,false);
  }
  // Brings the KV cache to `seq` and leaves the logits of its last position ready.
  // Returns how many leading positions were reused; -1 when cancelled.
  int Prefix(IList<int> seq,ref int steps){
   int reuse=0,n=seq.Count;
   while(reuse<n-1&&reuse<cachedCount&&cached[reuse]==seq[reuse])reuse++;
   for(int p=reuse;p<n;p++){
    if(Cancel){cachedCount=Math.Min(cachedCount,p);return -1;}
    Step(seq[p],p,p==n-1);steps++;
   }
   return reuse;
  }
  int Argmax(){int best=0;float m=logits[0];for(int i=1;i<VocabRows;i++)if(logits[i]>m){m=logits[i];best=i;}return best;}
  double LogSumExp(){
   float max=logits[0];for(int i=1;i<VocabRows;i++)if(logits[i]>max)max=logits[i];
   double sum=0;for(int i=0;i<VocabRows;i++)sum+=Math.Exp(logits[i]-max);
   return max+Math.Log(sum);
  }
  public void ResetCache(){cachedCount=0;}
  public void SelectCache(int index){
   if(index==slot)return;
   countSlots[slot]=cachedCount;slot=index;
   kc=kcSlots[index];vc=vcSlots[index];cached=cachedSlots[index];cachedCount=countSlots[index];
  }
  // Diagnostic: milliseconds for each of `count` consecutive steps from position `start`.
  public double[] StepTimes(int start,int count,bool wantLogits){
   double[] ms=new double[count];Stopwatch sw=new Stopwatch();
   pool.Begin();
   try{for(int i=0;i<count;i++){sw.Restart();Step(100+i,start+i,wantLogits);ms[i]=sw.Elapsed.TotalMilliseconds;}}
   finally{pool.End();cachedCount=0;}
   return ms;
  }

  // ---- conversion ------------------------------------------------------
  // Greedy first candidate. Sets StopReason to "cancelled" when Cancel was raised.
  public Result Convert(string context,string reading,int maxNewTokens){
   Stopwatch sw=Stopwatch.StartNew();
   Result res=new Result{Reading=reading,Context=context,Text="",StopReason="limit"};
   int[] prompt=Tokenize(Prompt(context,reading));
   int room=Context-prompt.Length-1;if(room<1)throw new ArgumentException("Input exceeds context");
   maxNewTokens=Math.Min(Math.Min(maxNewTokens,MaxNewTokens),room);
   res.PromptTokens=prompt.Length;
   pool.Begin();
   try{
    int steps=0;int reused=Prefix(prompt,ref steps);
    if(reused<0){res.StopReason="cancelled";return res;}
    res.ReusedTokens=reused;res.PrefillMilliseconds=sw.Elapsed.TotalMilliseconds;
    List<int> output=new List<int>();
    for(int n=0;n<maxNewTokens;n++){
     int best=Argmax();
     if(best==Eos){res.StopReason="eos";break;}
     output.Add(best);
     if(n+1>=maxNewTokens)break;
     if(Cancel){res.StopReason="cancelled";break;}
     Step(best,prompt.Length+n,true);
    }
    res.OutputTokens=output.Count;res.Text=Decode(output);
   }finally{pool.End();sw.Stop();res.Milliseconds=sw.Elapsed.TotalMilliseconds;}
   return res;
  }

  sealed class Hyp {public int[] Tokens;public float LogProb;}
  // Best-first search over one shared KV cache. Stops at `count` finished results or `stepBudget` model steps.
  public List<Candidate> Candidates(string context,string reading,int count,int stepBudget){
   List<Candidate> done=new List<Candidate>();
   int[] prompt=Tokenize(Prompt(context,reading));
   if(Context-prompt.Length-1<1)return done;
   int maxLen=Math.Min(MaxNewTokens,Context-prompt.Length-1);
   List<Hyp> open=new List<Hyp>();open.Add(new Hyp{Tokens=new int[0],LogProb=0});
   HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
   int steps=0;List<int> seq=new List<int>(prompt.Length+maxLen);
   pool.Begin();
   try{
    while(open.Count>0&&steps<stepBudget&&!Cancel){
     int bi=0;for(int i=1;i<open.Count;i++)if(open[i].LogProb>open[bi].LogProb)bi=i;
     Hyp h=open[bi];open.RemoveAt(bi);
     if(done.Count>=count){float worst=float.MaxValue;foreach(Candidate c in done)if(c.LogProb<worst)worst=c.LogProb;if(h.LogProb<worst)break;}
     seq.Clear();seq.AddRange(prompt);seq.AddRange(h.Tokens);
     if(Prefix(seq,ref steps)<0)break;
     double lse=LogSumExp();
     int width=h.Tokens.Length==0?Math.Max(count+2,8):4;float floor=(float)(-7.0);
     // top `width` tokens of this step
     int[] top=new int[width];float[] topLp=new float[width];int filled=0;
     for(int i=0;i<VocabRows;i++){
      float lp=(float)(logits[i]-lse);if(lp<floor)continue;
      if(filled<width){int p=filled++;while(p>0&&topLp[p-1]<lp){top[p]=top[p-1];topLp[p]=topLp[p-1];p--;}top[p]=i;topLp[p]=lp;}
      else if(lp>topLp[width-1]){int p=width-1;while(p>0&&topLp[p-1]<lp){top[p]=top[p-1];topLp[p]=topLp[p-1];p--;}top[p]=i;topLp[p]=lp;}
     }
     for(int i=0;i<filled;i++){
      float lp=h.LogProb+topLp[i];
      if(top[i]==Eos){
       if(h.Tokens.Length==0)continue;
       string text=Decode(h.Tokens);
       if(text.Length>0&&seen.Add(text))done.Add(new Candidate{Text=text,LogProb=lp});
      }else if(h.Tokens.Length<maxLen){
       int[] t=new int[h.Tokens.Length+1];Array.Copy(h.Tokens,t,h.Tokens.Length);t[t.Length-1]=top[i];
       open.Add(new Hyp{Tokens=t,LogProb=lp});
      }
     }
     if(open.Count>48){open.Sort(delegate(Hyp a,Hyp b){return b.LogProb.CompareTo(a.LogProb);});open.RemoveRange(48,open.Count-48);}
    }
   }finally{pool.End();}
   done.Sort(delegate(Candidate a,Candidate b){return b.LogProb.CompareTo(a.LogProb);});
   if(done.Count>count)done.RemoveRange(count,done.Count-count);
   return done;
  }
 }
}
