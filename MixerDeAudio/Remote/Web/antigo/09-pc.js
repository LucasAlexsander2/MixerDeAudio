/* ---------- aba PC: energia e monitores ---------- */
var ask_ok=null, plan=null, tick=null;
var NOME={desligar:'Desligar',reiniciar:'Reiniciar',suspender:'Suspender',bloquear:'Bloquear'};
var VERBO={desligar:'Desligar o PC',reiniciar:'Reiniciar o PC',suspender:'Suspender o PC (depois de suspenso, não dá para acordá-lo pelo celular)'};
var MODOS=[['principal','Só a tela do PC'],['segunda','Só a segunda tela'],['estender','Estender'],['duplicar','Duplicar']];
function ask(q,fn){ask_ok=fn;$('dq').textContent=q;$('dlg').hidden=false;}
$('dno').onclick=function(){ask_ok=null;$('dlg').hidden=true;};
$('dyes').onclick=function(){var f=ask_ok;ask_ok=null;$('dlg').hidden=true;if(f)f();};
function dur(n){var h=Math.floor(n/3600),mi=Math.floor(n%3600/60),s=n%60;function p(x){return x<10?'0'+x:''+x;}return (h?h+':'+p(mi):mi)+':'+p(s);}
function updPlan(){
  if(!plan){$('pwPlan').hidden=true;clearInterval(tick);tick=null;return;}
  var left=Math.max(0,Math.round((plan.at-Date.now())/1000));
  $('pwTxt').textContent=NOME[plan.acao]+' em '+dur(left);
  $('pwPlan').hidden=false;
}
Array.prototype.forEach.call(document.querySelectorAll('[data-pw]'),function(b){b.onclick=function(){
  var a=b.getAttribute('data-pw'),sel=$('pwSel'),seg=+sel.value;
  if(a==='bloquear'){send({t:'energia',acao:a,seg:seg});return;}
  ask(VERBO[a]+(seg?' '+sel.options[sel.selectedIndex].textContent.toLowerCase():' agora')+'?',function(){send({t:'energia',acao:a,seg:seg});});
};});
$('pwCancel').onclick=function(){send({t:'energiaCancelar'});};
MODOS.forEach(function(d){
  var b=el('button','',d[1]);b.setAttribute('data-m',d[0]);
  b.onclick=function(){send({t:'projecao',modo:d[0]});setTimeout(function(){send({t:'atualizar'});},1500);};
  $('projBtns').appendChild(b);
});
$('scOff').onclick=function(){send({t:'telas',acao:'apagar'});};
$('scOn').onclick=function(){send({t:'telas',acao:'acordar'});};
Array.prototype.forEach.call($('tabs').children,function(b){b.onclick=function(){fabChoose(b.getAttribute('data-tab'));};});
window.addEventListener('pagehide',function(){tlStop();});
