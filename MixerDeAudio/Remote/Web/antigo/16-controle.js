/* ---------- aba Controle: touchpad, texto, teclas e apresentação (Fase 9b) ---------- */
var inputOn=false, awakeOn=false, sens=1.6;
try{var sv=+localStorage.getItem('mxSens');if(sv>0)sens=sv;}catch(e){}
$('sens').value=String(sens);
if($('sens').value!==String(sens))$('sens').value='1.6';
$('sens').onchange=function(){sens=+this.value||1.6;try{localStorage.setItem('mxSens',this.value);}catch(e){}};
var pad=$('pad'),P={},pn=0,tp={t0:0,moved:0,max:0,drag:false,hold:0,iv:0,ax:0,ay:0,sx:0,sy:0};
function padFlush(){
  var dx=Math.round(tp.ax),dy=Math.round(tp.ay);
  if(dx||dy){send({t:'mouse',dx:dx,dy:dy});tp.ax-=dx;tp.ay-=dy;}
  var wx=Math.trunc(tp.sx/24),wy=Math.trunc(tp.sy/24);
  if(wx||wy){send({t:'rolar',dx:wx,dy:wy});tp.sx-=wx*24;tp.sy-=wy*24;}
}
function padReset(){
  if(!pn&&!tp.drag)return;
  clearTimeout(tp.hold);clearInterval(tp.iv);
  if(tp.drag)send({t:'botao',b:'esq',baixo:false});
  P={};pn=0;tp.drag=false;
}
pad.addEventListener('contextmenu',function(e){e.preventDefault();});
pad.addEventListener('pointerdown',function(e){
  e.preventDefault();
  try{pad.setPointerCapture(e.pointerId);}catch(x){}
  P[e.pointerId]={x:e.clientX,y:e.clientY};pn=Object.keys(P).length;
  if(pn===1){
    tp.t0=Date.now();tp.moved=0;tp.max=1;tp.drag=false;tp.ax=tp.ay=tp.sx=tp.sy=0;
    tp.hold=setTimeout(function(){if(pn===1&&tp.moved<10){tp.drag=true;send({t:'botao',b:'esq',baixo:true});if(navigator.vibrate)navigator.vibrate(20);}},450);
    tp.iv=setInterval(padFlush,30);
  }else{tp.max=Math.max(tp.max,pn);clearTimeout(tp.hold);}
});
pad.addEventListener('pointermove',function(e){
  var p=P[e.pointerId];if(!p)return;
  var dx=e.clientX-p.x,dy=e.clientY-p.y;p.x=e.clientX;p.y=e.clientY;
  tp.moved+=Math.abs(dx)+Math.abs(dy);
  if(pn===1){if(tp.max<2){tp.ax+=dx*sens;tp.ay+=dy*sens;}}
  else{tp.sx-=dx/pn;tp.sy+=dy/pn;}
});
function padEnd(e){
  if(!P[e.pointerId])return;
  delete P[e.pointerId];pn=Object.keys(P).length;
  if(pn>0)return;
  clearTimeout(tp.hold);clearInterval(tp.iv);padFlush();
  if(tp.drag)send({t:'botao',b:'esq',baixo:false});
  else if(e.type==='pointerup'&&tp.moved<10&&Date.now()-tp.t0<350)send({t:'clique',b:tp.max>=2?'dir':'esq'});
  tp.drag=false;
}
pad.addEventListener('pointerup',padEnd);
pad.addEventListener('pointercancel',padEnd);
$('bL').onclick=function(){send({t:'clique',b:'esq'});};
$('b2').onclick=function(){send({t:'clique',b:'esq',duplo:true});};
$('bR').onclick=function(){send({t:'clique',b:'dir'});};
$('txtGo').onclick=function(){var v=$('txt').value;if(!v)return;send({t:'texto',s:v});$('txt').value='';};
$('txt').addEventListener('keydown',function(e){if(e.key==='Enter'){e.preventDefault();$('txtGo').click();}});
function keyBtns(box,list){
  list.forEach(function(d){
    var b=el('button','',d[0]);
    b.onclick=function(){
      if(d[1]==='alt+f4'){ask('Fechar a janela da frente (Alt+F4)?',function(){send({t:'tecla',k:d[1]});});return;}
      send({t:'tecla',k:d[1]});
    };
    $(box).appendChild(b);
  });
}
keyBtns('kSlides',[['◀ Anterior','left'],['Próximo ▶','right'],['Começar (F5)','f5'],['Do slide atual','shift+f5'],['Tela preta (B)','b'],['Sair (Esc)','esc']]);
keyBtns('kVideo',[['⏯ Espaço','space'],['⏪ Voltar','left'],['⏩ Avançar','right'],['Vol +','up'],['Vol −','down'],['Tela cheia (F)','f'],['Mudo (M)','m']]);
keyBtns('kMedia',[['⏮','prev'],['⏯','play'],['⏭','next'],['Vol −','voldown'],['Vol +','volup'],['🔇','mute']]);
keyBtns('kKeys',[['Enter','enter'],['⌫','backspace'],['Tab','tab'],['Esc','esc'],['Espaço','space'],['Del','delete'],['Home','home'],['End','end'],['PgUp','pgup'],['PgDn','pgdn'],
  ['←','left'],['↑','up'],['↓','down'],['→','right'],['Win','win'],['Alt+Tab','alt+tab'],['Ctrl+C','ctrl+c'],['Ctrl+V','ctrl+v'],['Ctrl+X','ctrl+x'],['Ctrl+Z','ctrl+z'],
  ['Ctrl+A','ctrl+a'],['Ctrl+S','ctrl+s'],['Ctrl+F','ctrl+f'],['Ctrl+W','ctrl+w'],['Ctrl+T','ctrl+t'],['Alt+F4','alt+f4'],['Win+D','win+d'],['Tarefas','ctrl+shift+esc'],
  ['F1','f1'],['F2','f2'],['F3','f3'],['F4','f4'],['F5','f5'],['F6','f6'],['F7','f7'],['F8','f8'],['F9','f9'],['F10','f10'],['F11','f11'],['F12','f12']]);
$('awake').onclick=function(){send({t:'acordado',on:!awakeOn});};

