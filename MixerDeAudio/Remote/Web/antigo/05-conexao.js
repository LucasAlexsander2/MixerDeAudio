/* ---------- conexão ---------- */
function connect(){
  if(ws&&(ws.readyState===0||ws.readyState===1))return;
  clearTimeout(timer);timer=null;
  opened=false;
  var s;
  try{s=new WebSocket('ws://'+location.host+'/ws');}catch(e){retry();return;}
  ws=s;
  s.onopen=function(){opened=true;att=0;banner(false);};
  s.onmessage=function(e){
    var m;try{m=JSON.parse(e.data);}catch(x){return;}
    if(!m)return;
    if(m.t==='estado'){busyOn=false;S=m;if(screen!=='mixer')show('mixer');banner(false);render(m);renderPc(m);}
    else if(m.t==='ocupado'){busyOn=true;$('busyMsg').textContent='Outro celular está no controle'+(m.por?' ('+m.por+')':'')+'. Desconecte-o pelo PC ou por ele.';show('busy');}
    else if(m.t==='desconectado'){gone=true;}
    else if(m.t==='aviso'){toast(m.msg);}
    else if(m.t==='janelas'){renderWins(m.lista||[]);}
    else if(m.t==='clip'){$('clip').value=m.s||'';if(!m.s)toast('A área de transferência do PC está sem texto');}
    else if(m.t==='info'){renderInfo(m);}
  };
  s.onclose=function(e){
    if(ws!==s)return;
    ws=null;tlStop();padReset();winSub=false;infoSub=false;
    if(gone||e.code===4000||e.code===4001){
      gone=true;
      $('goneMsg').textContent=e.code===4001?'Esta página foi substituída por outra conexão deste celular.':'Este celular foi desconectado do PC.';
      show('gone');return;
    }
    if(busyOn||e.code===4009){busyOn=true;if(screen!=='busy')show('busy');retry(5000);return;}
    if(opened){banner(true,'Reconectando…');retry();return;}
    /* nunca abriu: só um 401 de /sessao (cookie ausente ou chave desconhecida) leva ao pareamento (Fase 10b, D11);
       sem resposta, 5xx e qualquer outro status ficam em "Reconectando…" (espelha RemoteRules.DecidePage) */
    fetch('/sessao',{cache:'no-store'}).then(function(r){
      if(r.status===401){att=0;show('pair');$('code').focus();}else{failed();}
    }).catch(failed);
  };
}
function failed(){if(screen!=='pair')banner(true,'Reconectando…');retry();}
/* espera crescente: 0,5 s, 1 s, 2 s, 4 s, 5 s (espelha RemoteRules.ReconnectDelayMs) */
function retry(ms){if(timer)return;if(!ms){ms=Math.min(5000,500<<Math.min(att,4));att++;}timer=setTimeout(function(){timer=null;connect();},ms);}
document.addEventListener('visibilitychange',function(){
  if(document.visibilityState==='hidden'){tlStop();padReset();}
  if(document.visibilityState==='visible'&&(screen==='mixer'||screen==='busy')&&!(ws&&ws.readyState===1)){clearTimeout(timer);timer=null;connect();}
});
$('again').onclick=function(){gone=false;busyOn=false;banner(true,'Conectando…');connect();};
$('bye').onclick=function(){send({t:'desconectar'});};
function send(o){if(ws&&ws.readyState===1)ws.send(JSON.stringify(o));}

