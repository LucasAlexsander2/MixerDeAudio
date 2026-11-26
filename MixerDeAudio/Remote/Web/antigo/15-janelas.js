/* ---------- aba Janelas e atalhos (Fase 9c, D21 a D23): a lista só é pedida com a aba aberta ---------- */
var jnOn=false, winSub=false, winSig='', scSig='';
function winSync(){
  var want=screen==='mixer'&&tab==='jn'&&jnOn;
  if(want===winSub)return;
  winSub=want;winSig='';
  send({t:'assinar',g:'janelas',on:want});
}
function winAct(id,acao){send({t:'janela',id:id,acao:acao});setTimeout(function(){send({t:'janelas'});},900);}
function renderWins(list){
  var sg=JSON.stringify(list);
  if(sg===winSig)return;
  winSig=sg;
  var box=$('winList');box.textContent='';
  $('winEmpty').hidden=list.length>0;
  list.forEach(function(w){
    var c=el('div','card win'+(w.min?' min':'')),top=el('div','top'),info=el('div','info');
    var ico=el('span','ico',(w.proc||w.titulo||'?').charAt(0).toUpperCase());
    if(w.icone){
      var img=new Image();img.alt='';img.width=img.height=32;img.className='ico';
      img.onerror=function(){img.replaceWith(ico);};
      img.src='/icone/'+encodeURIComponent(w.icone);
      top.appendChild(img);
    }else top.appendChild(ico);
    info.append(el('div','name',w.titulo),el('div','sub',(w.proc||'')+(w.min?' · minimizada':'')));
    top.appendChild(info);
    var row=el('div','row3'),b1=el('button','','Frente'),b2=el('button','','Minimizar'),b3=el('button','','Fechar');
    b1.onclick=function(){winAct(w.id,'frente');};
    b2.onclick=function(){winAct(w.id,'minimizar');};
    b3.onclick=function(){ask('Fechar "'+w.titulo+'"? O programa pode perguntar se você quer salvar.',function(){winAct(w.id,'fechar');});};
    row.append(b1,b2,b3);
    c.append(top,row);box.appendChild(c);
  });
}
function renderShortcuts(list){
  var sg=JSON.stringify(list);
  if(sg===scSig)return;
  scSig=sg;
  var box=$('scList');box.textContent='';
  $('scEmpty').hidden=list.length>0;
  list.forEach(function(n){
    var b=el('button','',n);
    b.onclick=function(){send({t:'atalho',nome:n});toast('Abrindo '+n+'…');};
    box.appendChild(b);
  });
}

