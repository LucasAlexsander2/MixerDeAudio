/* ---------- menu flutuante (Parte 10c, D15 a D19) ---------- */
var fab={o:false,s:'r',y:.7,x:0,py:0,hv:-1,pts:[],bs:[],st:null,idle:null};
try{var fj=JSON.parse(localStorage.getItem('mx.fab'));if(fj&&(fj.s==='l'||fj.s==='r')&&fj.y>=0&&fj.y<=1){fab.s=fj.s;fab.y=fj.y;}}catch(e){}
/* pura: centros das n opções em leque, num arco virado para o centro da tela, sempre dentro dela */
function fabArc(cx,cy,W,H,n,R,top,bot){
  var base=Math.atan2(H/2-cy,W/2-cx),r,k,g,a,i,p,u,T=[[1,2.6],[1.25,2],[1.6,1.45]];
  for(u=0;u<3;u++)for(k=0;k<=18;k++)for(g=0;g<2;g++){
    var Q=R*T[u][0],st=n>1?Math.min(.62,T[u][1]/(n-1)):0;
    a=base+(g?-1:1)*k*.1745;r=[];p=true;
    for(i=0;i<n;i++){var t=a+(i-(n-1)/2)*st,x=cx+Q*Math.cos(t),y=cy+Q*Math.sin(t);
      if(x<30||x>W-30||y<top+30||y>H-bot-34)p=false;r.push([x,y]);}
    if(p)return r;
  }
  return r.map(function(q){return[Math.min(Math.max(q[0],30),W-30),Math.min(Math.max(q[1],top+30),H-bot-34)];});
}
/* pura: índice da opção sob o dedo (raio 40) ou -1 */
function fabPick(pts,x,y){var b=-1,d=1600,i,q;for(i=0;i<pts.length;i++){q=(pts[i][0]-x)*(pts[i][0]-x)+(pts[i][1]-y)*(pts[i][1]-y);if(q<=d){d=q;b=i;}}return b;}
window.fabArc=fabArc;window.fabPick=fabPick;
function fabIns(){var c=getComputedStyle($('fabp'));return{t:parseFloat(c.paddingTop)||0,r:parseFloat(c.paddingRight)||0,b:parseFloat(c.paddingBottom)||0,l:parseFloat(c.paddingLeft)||0};}
function fabSet(x,y){var f=$('fab');fab.x=x;fab.py=y;f.style.setProperty('--px',x+'px');f.style.setProperty('--py',y+'px');}
function fabClamp(x,y){var n=fabIns(),W=innerWidth,H=innerHeight;return[Math.min(Math.max(x,n.l+6),W-n.r-62),Math.min(Math.max(y,n.t+6),H-n.b-62)];}
function fabPlace(){
  var n=fabIns(),c=fabClamp(fab.s==='r'?innerWidth-n.r-62:n.l+6,fab.y*innerHeight-28);
  var f=$('fab');
  if(!fab.placed){fab.placed=true;f.classList.add('drag');fabSet(c[0],c[1]);void f.offsetWidth;f.classList.remove('drag');}
  else fabSet(c[0],c[1]);
  fabWake();
}
function fabWake(){var f=$('fab');f.classList.remove('idle');clearTimeout(fab.idle);fab.idle=setTimeout(function(){if(!fab.o&&!fab.st)f.classList.add('idle');},3000);}
function fabOpen(){
  if(fab.o)return;fab.o=true;
  var cx=fab.x+28,cy=fab.py+28,n=fabIns(),bs=[],t=$('tabs');
  Array.prototype.forEach.call(t.children,function(b){if(!b.hidden)bs.push(b);});
  fab.pts=fabArc(cx,cy,innerWidth,innerHeight,bs.length,120,n.t,n.b);fab.bs=bs;fab.hv=-1;
  bs.forEach(function(b,i){b.style.setProperty('--x',(fab.pts[i][0]-26)+'px');b.style.setProperty('--y',(fab.pts[i][1]-26)+'px');b.style.setProperty('--d',(i*25)+'ms');});
  Array.prototype.forEach.call(t.children,function(b){b.style.setProperty('--fx',(cx-26)+'px');b.style.setProperty('--fy',(cy-26)+'px');});
  t.classList.add('open');$('veil').classList.add('open');$('fab').classList.remove('idle');
}
function fabClose(){
  if(!fab.o)return;fab.o=false;
  $('tabs').classList.remove('open');$('veil').classList.remove('open');
  fab.bs.forEach(function(b){b.classList.remove('hv');});fab.hv=-1;fabWake();
}
function fabHover(i){
  if(i===fab.hv)return;
  fab.bs.forEach(function(b,j){b.classList.toggle('hv',j===i);});fab.hv=i;
}
function fabChoose(k){
  fabClose();tab=k;tabBtn();view();send({t:'atualizar'});
  var m=$(k==='som'?'mixer':k);m.classList.remove('ent');void m.offsetWidth;m.classList.add('ent');
}
(function(){
  var f=$('fab');
  f.onpointerdown=function(e){
    e.preventDefault();e.stopPropagation();
    try{f.setPointerCapture(e.pointerId);}catch(x){}
    var s=fab.st={x:e.clientX,y:e.clientY,moved:false,move:false,was:fab.o,dx:e.clientX-fab.x,dy:e.clientY-fab.py};
    f.classList.remove('idle');
    if(!fab.o)fabOpen();
    s.t=setTimeout(function(){
      if(fab.st!==s||s.moved)return;
      s.move=true;fabClose();f.classList.add('drag','lift');
      try{if(navigator.vibrate)navigator.vibrate(15);}catch(x){}
    },600);
  };
  f.onpointermove=function(e){
    var s=fab.st;if(!s)return;e.preventDefault();
    if(!s.moved&&Math.abs(e.clientX-s.x)+Math.abs(e.clientY-s.y)>10){s.moved=true;if(!s.move)clearTimeout(s.t);}
    if(s.move){var c=fabClamp(e.clientX-s.dx,e.clientY-s.dy);fabSet(c[0],c[1]);}
    else if(s.moved&&fab.o)fabHover(fabPick(fab.pts,e.clientX,e.clientY));
  };
  function end(e,cancel){
    var s=fab.st;if(!s)return;fab.st=null;clearTimeout(s.t);
    try{f.releasePointerCapture(e.pointerId);}catch(x){}
    if(s.move){
      f.classList.remove('drag','lift');
      fab.s=fab.x+28<innerWidth/2?'l':'r';fab.y=Math.min(1,Math.max(0,(fab.py+28)/innerHeight));
      try{localStorage.setItem('mx.fab',JSON.stringify({s:fab.s,y:fab.y}));}catch(x){}
      fabPlace();return;
    }
    var h=fab.hv;
    if(cancel)fabClose();
    else if(s.moved){if(h>=0)fabChoose(fab.bs[h].getAttribute('data-tab'));else fabClose();}
    else if(s.was)fabClose();
    fabWake();
  }
  f.onpointerup=function(e){end(e,false);};
  f.onpointercancel=function(e){end(e,true);};
  f.oncontextmenu=function(e){e.preventDefault();};
  $('veil').onclick=fabClose;
  document.addEventListener('keydown',function(e){if(e.key==='Escape')fabClose();});
  window.addEventListener('resize',function(){if(!$('fab').hidden){fabClose();fabPlace();}});
})();

function tabBtn(){Array.prototype.forEach.call($('tabs').children,function(b){var on=b.getAttribute('data-tab')===tab;b.classList.toggle('on',on);if(on)$('fab').replaceChildren(b.firstChild.cloneNode(true));});}
function renderPc(s){
  var f=s.ferr||{},pc=s.pc||{};
  var avail={pc:!!(f.energia||f.monitores||f.estado),ctl:!!f.entrada,tl:!!f.tela,jn:!!(f.janelas||f.atalhos),tf:!!f.transferencia};
  Array.prototype.forEach.call($('tabs').children,function(b){var k=b.getAttribute('data-tab');b.hidden=k!=='som'&&!avail[k];});
  tabsOn=avail.pc||avail.ctl||avail.tl||avail.jn||avail.tf;
  if(tab!=='som'&&!avail[tab])tab='som';
  inputOn=!!f.entrada;jnOn=!!f.janelas;infoOn=!!f.estado;
  tabBtn();view();
  $('secPower').hidden=!f.energia;
  $('secDisp').hidden=!f.monitores;
  $('secSleep').hidden=!f.estado;
  $('secInfo').hidden=!f.estado;
  $('secWin').hidden=!f.janelas;
  $('secSc').hidden=!f.atalhos;
  var sl=pc.sono;
  if(f.estado&&sl){slPlan={at:Date.now()+(sl.rest||0)*1000};if(!slTick)slTick=setInterval(updSleep,1000);}
  else slPlan=null;
  updSleep();
  if(f.atalhos)renderShortcuts(pc.atalhos||[]);
  var e=pc.energia;
  if(f.energia&&e){plan={acao:e.acao,at:Date.now()+(e.rest||0)*1000};if(!tick)tick=setInterval(updPlan,1000);}
  else plan=null;
  updPlan();
  var pj=pc.projecao||{},ok=pj.telas>=2;
  Array.prototype.forEach.call($('projBtns').children,function(b){b.disabled=!ok;b.classList.toggle('on',b.getAttribute('data-m')===pj.modo);b.setAttribute('aria-pressed',b.getAttribute('data-m')===pj.modo);});
  if(f.monitores)renderBrilho(pc.brilho||[]);
  $('projInfo').textContent=ok?pj.telas+' telas ligadas ao PC.':'Só há uma tela ligada ao PC.';
  awakeOn=!!pc.acordado;
  $('awake').classList.toggle('on',awakeOn);$('awake').setAttribute('aria-pressed',awakeOn);
  $('awake').textContent=awakeOn?'Tela ligada: tocar para desligar':'Manter a tela ligada';
  $('tlClk').hidden=$('tlTap').hidden=!inputOn;
  $('tlBarC1').hidden=$('tlBarC3').hidden=$('tlBarKb').hidden=!inputOn;
  renderMons(pc.monitores||[]);
}

