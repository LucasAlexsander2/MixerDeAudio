/* ---------- aba Tela: só transmite depois de tocar em "Mostrar a tela" (D18) ----------
   Fase 10: um canal WebSocket próprio (/tela/ws), um quadro por vez (a página manda {"ok":1} depois de desenhar),
   desenho em <canvas>, três qualidades, tela cheia e zoom com dois dedos. */
var sc={on:false,ws:null,q:'normal',mons:'',fs:false,nat:false,iw:0,ih:0,z:1,tx:0,ty:0,url:null,got:false,pt:null,barT:0,cfgT:0,kb:false};
try{var sq=localStorage.getItem('mx.tlq');if(sq==='eco'||sq==='normal'||sq==='alta')sc.q=sq;}catch(e){}
var QNOME={eco:'Econômica',normal:'Normal',alta:'Alta'};
var tlCv=$('tlCv'),tlCtx=tlCv.getContext?tlCv.getContext('2d'):null,tlHas=!!(window.createImageBitmap&&tlCtx);
tlCv.hidden=!tlHas;$('tlImg').hidden=tlHas;
function tlClamp(v,a,b){return v<a?a:v>b?b:v;}
function tlUi(){$('telaIniciar').hidden=sc.on;$('tlView').hidden=!sc.on;}
function tlQUi(){
  var b=document.querySelectorAll('#tlQ button');
  for(var i=0;i<b.length;i++)b[i].classList.toggle('on',b[i].getAttribute('data-q')===sc.q);
  $('tlBarQ').textContent=QNOME[sc.q];
}
function tlSendWs(o){if(sc.ws&&sc.ws.readyState===1)sc.ws.send(JSON.stringify(o));}
/* lado maior da área visível × densidade (D3); o servidor corta pela qualidade e pela tela */
function tlWidth(){
  var side=sc.fs?Math.max(window.innerWidth,window.innerHeight):($('tlStage').clientWidth||window.innerWidth);
  return Math.max(160,Math.round(side*(window.devicePixelRatio||1)));
}
function tlConfig(){tlSendWs({mon:+$('tlMon').value||0,w:tlWidth(),q:sc.q});}
function tlConfigSoon(){clearTimeout(sc.cfgT);sc.cfgT=setTimeout(tlConfig,300);}
function tlSetQ(q){
  sc.q=q;try{localStorage.setItem('mx.tlq',q);}catch(e){}
  tlQUi();tlConfig();
}

/* Conta do ponto (D8), numa função só: (cx,cy) = toque em px dentro do palco (sw×sh); imagem iw×ih desenhada com
   "contain" (faixas pretas); zoom z e deslocamento tx,ty (transform: translate(tx,ty) scale(z), origem 0,0).
   Devolve x,y de 0 a 1 dentro da imagem (cortados nas bordas). */
function tlPoint(cx,cy,sw,sh,iw,ih,z,tx,ty){
  var s=Math.min(sw/iw,sh/ih),dw=iw*s,dh=ih*s,ox=(sw-dw)/2,oy=(sh-dh)/2;
  var ux=(cx-tx)/z,uy=(cy-ty)/z;
  return {x:tlClamp((ux-ox)/dw,0,1),y:tlClamp((uy-oy)/dh,0,1)};
}
/* inverso: ponto 0..1 da imagem → px no palco (para a bolinha) */
function tlScreenXY(fx,fy,sw,sh,iw,ih,z,tx,ty){
  var s=Math.min(sw/iw,sh/ih),dw=iw*s,dh=ih*s,ox=(sw-dw)/2,oy=(sh-dh)/2;
  return {x:tx+z*(ox+fx*dw),y:ty+z*(oy+fy*dh)};
}
window.tlPoint=tlPoint;

function tlApply(){
  var st=$('tlStage'),sw=st.clientWidth,sh=st.clientHeight;
  sc.z=tlClamp(sc.z,1,4);
  sc.tx=tlClamp(sc.tx,sw-sw*sc.z,0);sc.ty=tlClamp(sc.ty,sh-sh*sc.z,0);
  $('tlZoom').style.transform='translate('+sc.tx+'px,'+sc.ty+'px) scale('+sc.z+')';
  var d=$('tlDot');
  if(sc.pt&&sc.iw){var p=tlScreenXY(sc.pt.x,sc.pt.y,sw,sh,sc.iw,sc.ih,sc.z,sc.tx,sc.ty);d.style.left=p.x+'px';d.style.top=p.y+'px';d.hidden=false;}
  else d.hidden=true;
}
function tlZoomReset(){sc.z=1;sc.tx=0;sc.ty=0;tlApply();}

function tlSized(w,h){
  if(sc.iw===w&&sc.ih===h)return;
  sc.iw=w;sc.ih=h;
  $('tlStage').style.setProperty('--ar',w+'/'+h);
  tlApply();
}
function tlAck(){tlSendWs({ok:1});}
function tlDraw(b){
  if(tlHas){
    window.createImageBitmap(b).then(function(bm){
      if(!sc.on){bm.close();return;}
      if(tlCv.width!==bm.width||tlCv.height!==bm.height){tlCv.width=bm.width;tlCv.height=bm.height;}
      tlCtx.drawImage(bm,0,0);
      var w=bm.width,h=bm.height;
      bm.close();
      tlSized(w,h);tlAck();
    }).catch(function(){tlStop('Tela indisponível.');});
    return;
  }
  var u=URL.createObjectURL(b),im=$('tlImg');
  im.onload=function(){
    im.onload=null;
    if(!sc.on){URL.revokeObjectURL(u);return;}
    if(sc.url)URL.revokeObjectURL(sc.url);
    sc.url=u;tlSized(im.naturalWidth,im.naturalHeight);tlAck();
  };
  im.onerror=function(){URL.revokeObjectURL(u);tlStop('Tela indisponível.');};
  im.src=u;
}
function tlStop(msg){
  if(!sc.on&&!msg)return;
  sc.on=false;clearTimeout(sc.cfgT);clearTimeout(sc.barT);
  var w=sc.ws;sc.ws=null;
  if(w){w.onopen=w.onmessage=w.onclose=w.onerror=null;try{w.close();}catch(e){}}
  tlFsExit();
  $('tlImg').removeAttribute('src');$('tlDot').hidden=true;
  if(tlCtx){tlCv.width=1;tlCv.height=1;}
  if(sc.url){URL.revokeObjectURL(sc.url);sc.url=null;}
  sc.iw=sc.ih=0;sc.pt=null;sc.got=false;sc.z=1;sc.tx=sc.ty=0;
  $('tlMsg').textContent=msg||'';
  tlUi();
}
/* WebSocket recusado: o navegador não mostra o status HTTP; um GET comum à mesma rota repete as regras (401, 409, 403) */
function tlWhy(){
  return fetch('/tela/ws',{cache:'no-store'}).then(function(r){
    return r.status===409?'Outro celular está no controle.':r.status===403?'A tela não está liberada no PC.':'Tela indisponível.';
  }).catch(function(){return 'Tela indisponível.';});
}
function tlStart(){
  if(sc.on)return;
  sc.on=true;sc.got=false;$('tlMsg').textContent='';tlUi();tlQUi();
  var w;
  try{w=new WebSocket('ws://'+location.host+'/tela/ws');}catch(e){tlStop('Tela indisponível.');return;}
  sc.ws=w;
  w.binaryType='blob';
  w.onopen=function(){if(sc.ws===w)tlConfig();};
  w.onmessage=function(e){
    if(sc.ws!==w||typeof e.data==='string')return;
    sc.got=true;
    tlDraw(e.data);
  };
  w.onclose=function(e){
    if(sc.ws!==w||!sc.on)return;
    if(e.code===4003)return tlStop('A tela não está liberada no PC.');
    if(e.code===4004)return tlStop('Tela indisponível.');
    if(sc.got)return tlStop('Tela indisponível.');
    tlWhy().then(function(m){if(sc.ws===w&&sc.on)tlStop(m);});
  };
}
$('telaIniciar').onclick=tlStart;
$('telaParar').onclick=function(){tlStop();};
$('tlMon').onchange=function(){sc.pt=null;tlZoomReset();tlConfig();};
$('tlQ').addEventListener('click',function(e){var q=e.target&&e.target.getAttribute&&e.target.getAttribute('data-q');if(q)tlSetQ(q);});
window.addEventListener('resize',function(){if(sc.on){tlApply();tlConfigSoon();}});

/* ---------- tela cheia (D7) ---------- */
function tlBarShow(){
  var b=$('tlBar');b.classList.remove('hide');clearTimeout(sc.barT);
  if(sc.fs&&!sc.kb)sc.barT=setTimeout(function(){b.classList.add('hide');},3000);
}
function tlFsEnter(){
  if(sc.fs||!sc.on)return;
  var box=$('tlBox'),rq=box.requestFullscreen||box.webkitRequestFullscreen;
  sc.fs=true;sc.nat=false;
  box.classList.add('fs');document.body.classList.add('tlfs');
  if(rq){
    try{
      var p=rq.call(box);
      var lock=function(){try{if(screen.orientation&&screen.orientation.lock){var r=screen.orientation.lock('landscape');if(r&&r.catch)r.catch(function(){});}}catch(e){}};
      if(p&&p.then)p.then(lock,function(){});else lock();
    }catch(e){}
  }else if(window.innerHeight>window.innerWidth){
    var h=$('tlRot');h.hidden=false;setTimeout(function(){h.hidden=true;},4000);
  }
  $('tlKb').hidden=true;sc.kb=false;
  tlZoomReset();tlBarShow();tlConfigSoon();
}
function tlFsExit(){
  if(!sc.fs)return;
  sc.fs=false;sc.kb=false;
  var box=$('tlBox');
  box.classList.remove('fs');document.body.classList.remove('tlfs');
  $('tlRot').hidden=true;$('tlKb').hidden=true;
  clearTimeout(sc.barT);
  try{if(document.fullscreenElement&&document.exitFullscreen)document.exitFullscreen();else if(document.webkitFullscreenElement&&document.webkitExitFullscreen)document.webkitExitFullscreen();}catch(e){}
  try{if(screen.orientation&&screen.orientation.unlock)screen.orientation.unlock();}catch(e){}
  tlZoomReset();
  if(sc.on)tlConfigSoon();
}
function tlFsChanged(){
  var el=document.fullscreenElement||document.webkitFullscreenElement;
  if(el===$('tlBox'))sc.nat=true;
  else if(sc.fs&&sc.nat)tlFsExit();
}
document.addEventListener('fullscreenchange',tlFsChanged);
document.addEventListener('webkitfullscreenchange',tlFsChanged);
document.addEventListener('keydown',function(e){if(e.key==='Escape'&&sc.fs)tlFsExit();});
$('tlFs').onclick=tlFsEnter;
$('tlBarOut').onclick=tlFsExit;
$('tlBarQ').onclick=function(){tlSetQ(sc.q==='eco'?'normal':sc.q==='normal'?'alta':'eco');tlBarShow();};
$('tlBarC1').onclick=function(){send({t:'clique',b:'esq'});tlBarShow();};
$('tlBarC3').onclick=function(){send({t:'clique',b:'dir'});tlBarShow();};
$('tlBarKb').onclick=function(){
  sc.kb=!sc.kb;$('tlKb').hidden=!sc.kb;
  if(sc.kb){clearTimeout(sc.barT);$('tlTxt').focus();}else tlBarShow();
};
function tlKbSend(){var v=$('tlTxt').value;if(!v)return;send({t:'texto',s:v});$('tlTxt').value='';}
$('tlTxtGo').onclick=tlKbSend;
$('tlTxt').addEventListener('keydown',function(e){if(e.key==='Enter'){e.preventDefault();tlKbSend();}});

/* ---------- toque, pinça e arraste (D8) ---------- */
var tlPtr={},tlG=null,tlGestured=false,tlLast={t:0,x:0,y:0};
function tlN(){var n=0;for(var k in tlPtr)n++;return n;}
function tlTap(x,y){
  var st=$('tlStage'),now=Date.now();
  if(sc.fs){
    if(sc.z>1&&now-tlLast.t<350&&Math.abs(x-tlLast.x)<30&&Math.abs(y-tlLast.y)<30){tlLast.t=0;tlZoomReset();return;}
    tlLast={t:now,x:x,y:y};tlBarShow();
  }
  if(!inputOn||!sc.iw)return;
  var p=tlPoint(x,y,st.clientWidth,st.clientHeight,sc.iw,sc.ih,sc.z,sc.tx,sc.ty);
  send({t:'mouseAbs',mon:+$('tlMon').value||0,x:p.x,y:p.y});
  sc.pt=p;tlApply();
}
(function(){
  var st=$('tlStage');
  function rel(e){var r=st.getBoundingClientRect();return {x:e.clientX-r.left,y:e.clientY-r.top};}
  st.addEventListener('pointerdown',function(e){
    try{st.setPointerCapture(e.pointerId);}catch(x){}
    var p=rel(e);tlPtr[e.pointerId]={x:p.x,y:p.y,sx:p.x,sy:p.y,t:Date.now()};
    if(tlN()>=2){
      tlGestured=true;
      if(sc.fs){
        var ids=Object.keys(tlPtr),a=tlPtr[ids[0]],b=tlPtr[ids[1]];
        tlG={d0:Math.max(1,Math.hypot(a.x-b.x,a.y-b.y)),mx:(a.x+b.x)/2,my:(a.y+b.y)/2,z0:sc.z,tx0:sc.tx,ty0:sc.ty};
      }
    }
  });
  st.addEventListener('pointermove',function(e){
    var q=tlPtr[e.pointerId];if(!q)return;
    var p=rel(e);q.x=p.x;q.y=p.y;
    if(tlG&&tlN()>=2){
      var ids=Object.keys(tlPtr),a=tlPtr[ids[0]],b=tlPtr[ids[1]];
      var d=Math.hypot(a.x-b.x,a.y-b.y),mx=(a.x+b.x)/2,my=(a.y+b.y)/2;
      var z=tlClamp(tlG.z0*d/tlG.d0,1,4);
      /* o ponto da imagem que estava sob o meio dos dedos continua sob ele */
      var ux=(tlG.mx-tlG.tx0)/tlG.z0,uy=(tlG.my-tlG.ty0)/tlG.z0;
      sc.z=z;sc.tx=mx-z*ux;sc.ty=my-z*uy;
      tlApply();
    }else if(Math.hypot(q.x-q.sx,q.y-q.sy)>10)q.moved=true;
  });
  function up(e,cancel){
    var q=tlPtr[e.pointerId];if(!q)return;
    delete tlPtr[e.pointerId];
    if(tlN()<2)tlG=null;
    if(tlN()===0){
      var tap=!cancel&&!tlGestured&&!q.moved&&Date.now()-q.t<600;
      tlGestured=false;
      if(tap)tlTap(q.x,q.y);
    }
  }
  st.addEventListener('pointerup',function(e){up(e,false);});
  st.addEventListener('pointercancel',function(e){up(e,true);});
})();
$('tlC1').onclick=function(){send({t:'clique',b:'esq'});};
$('tlC2').onclick=function(){send({t:'clique',b:'esq',duplo:true});};
$('tlC3').onclick=function(){send({t:'clique',b:'dir'});};
function renderMons(list){
  var sg=JSON.stringify(list);
  if(sc.mons===sg)return;
  sc.mons=sg;
  var sel=$('tlMon'),cur=sel.value;sel.textContent='';
  list.forEach(function(m){
    var o=el('option','','Tela '+(m.i+1)+(m.principal?' (principal)':'')+' · '+m.w+'×'+m.h);o.value=m.i;sel.appendChild(o);
  });
  if(cur&&sel.querySelector('option[value="'+cur+'"]'))sel.value=cur;
  sel.hidden=list.length<2;
}

