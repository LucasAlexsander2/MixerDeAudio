/* ---------- aba Enviar: área de transferência e arquivo (Fase 9d, D25 e D26) ---------- */
$('clipRead').onclick=function(){send({t:'clipLer'});};
$('clipSend').onclick=function(){
  var v=$('clip').value;
  if(v.length>4000){toast('Máximo de 4.000 caracteres por envio');return;}
  send({t:'clipGravar',s:v});toast('Enviado ao PC');
};
$('clipCopy').onclick=function(){
  var t=$('clip'),ok=false;t.focus();t.select();
  try{t.setSelectionRange(0,t.value.length);ok=document.execCommand('copy');}catch(e){}
  toast(ok?'Copiado no celular':'Texto selecionado: copie à mão');
};
var fileXhr=null,FERR={401:'Pareamento recusado.',403:'O envio de arquivos está desligado no PC.',409:'Outro envio em andamento ou este celular não está no controle.',
  411:'O navegador não informou o tamanho.',413:'Arquivo grande demais (máximo 500 MB).',507:'Sem espaço livre no PC.'};
function mb(n){return n>=1048576?(n/1048576).toFixed(1).replace('.',',')+' MB':Math.max(1,Math.round(n/1024))+' KB';}
$('fileIn').onchange=function(){
  var f=this.files[0];$('fileGo').disabled=!f||!!fileXhr;
  $('fileMsg').textContent=f?f.name+' ('+mb(f.size)+')':'';
};
$('fileGo').onclick=function(){
  var f=$('fileIn').files[0];if(!f||fileXhr)return;
  if(f.size>524288000){$('fileMsg').textContent=FERR[413];return;}
  var x=new XMLHttpRequest();fileXhr=x;
  function end(msg,ok){fileXhr=null;$('fileBar').hidden=true;$('fileMsg').textContent=msg;if(ok)$('fileIn').value='';$('fileGo').disabled=!$('fileIn').files[0];}
  x.open('POST','/arquivo?nome='+encodeURIComponent(f.name));
  x.upload.onprogress=function(e){if(e.lengthComputable){$('fileBar').hidden=false;$('fileBar').value=Math.round(e.loaded*100/e.total);}};
  x.onload=function(){
    var r={};try{r=JSON.parse(x.responseText);}catch(e){}
    if(x.status===200)end('Enviado: '+(r.nome||f.name),true);else end(FERR[x.status]||'Falha ao enviar ('+x.status+').');
  };
  x.onerror=x.ontimeout=x.onabort=function(){end('A conexão caiu durante o envio.');};
  $('fileGo').disabled=true;$('fileMsg').textContent='Enviando…';
  x.send(f);
};

