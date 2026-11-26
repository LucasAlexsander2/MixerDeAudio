/* ---------- desenho ---------- */
function muteIcon(b,muted){b.textContent=muted?'🔇':'🔊';b.classList.toggle('muted',muted);b.setAttribute('aria-pressed',muted);}
function el(tag,cls,text){var e=document.createElement(tag);if(cls)e.className=cls;if(text!==undefined)e.textContent=text;return e;}

function render(s){
  /* saídas */
  var os=JSON.stringify(s.saidas);
  if(sig.outs!==os){
    sig.outs=os;var o=$('outs');o.textContent='';
    s.saidas.forEach(function(d){
      var b=el('button',d.ativa?'on':'');b.setAttribute('aria-label',d.nome);b.setAttribute('aria-pressed',d.ativa);
      b.appendChild(el('span','g',G[d.glifo]||'🔊'));b.appendChild(el('span','n',d.nome));
      b.onclick=function(){send({t:'padrao',id:d.id});};
      o.appendChild(b);
    });
  }
  /* geral */
  var g=s.geral,ms=$('ms'),mm=$('mm');
  $('master').classList.toggle('off',!g.disponivel);
  ms.disabled=mm.disabled=!g.disponivel;
  setSlider(ms,$('mv'),'g',g.volume);muteIcon(mm,g.mudo);
  /* apps */
  var box=$('apps'),seen={};
  s.apps.forEach(function(a,i){
    seen[a.chave]=1;
    var r=rows[a.chave];
    if(!r)r=rows[a.chave]=makeRow(a);
    r.name.textContent=a.nome;
    setSlider(r.sl,r.val,a.chave,a.volume);
    r.mute.hidden=r.line.hidden=!!a.soMidia;
    if(!a.soMidia)muteIcon(r.mute,a.mudo);
    mediaRow(r,a);
    r.mute.setAttribute('aria-label','Mudo: '+a.nome);r.sl.setAttribute('aria-label','Volume de '+a.nome);
    var rs=JSON.stringify(a.rotas);
    if(r.sig!==rs){
      r.sig=rs;r.routes.textContent='';r.routes.hidden=!a.rotas.length;
      a.rotas.forEach(function(d){
        var b=el('button',d.ativa?'on':'');b.setAttribute('aria-label','Enviar '+a.nome+' para '+d.nome);b.setAttribute('aria-pressed',d.ativa);
        b.appendChild(el('span','rg',G[d.glifo]||'🔊'));b.appendChild(el('span','rn',d.nome));
        b.onclick=function(){send({t:'rota',app:a.chave,id:d.id});};
        r.routes.appendChild(b);
      });
    }
    if(box.children[i]!==r.root)box.insertBefore(r.root,box.children[i]||null);
  });
  Object.keys(rows).forEach(function(k){if(!seen[k]){rows[k].root.remove();delete rows[k];}});
  $('empty').hidden=s.apps.length>0;
}
function mediaRow(r,a){
  var md=a.midia;
  r.media.hidden=!md;
  if(!md)return;
  r.prev.disabled=!md.ant;r.next.disabled=!md.prox;r.pp.disabled=!md.pp;
  r.pp.textContent=md.tocando?'⏸':'▶';
  r.pp.setAttribute('aria-label',(md.tocando?'Pausar ':'Tocar ')+a.nome);
  r.prev.setAttribute('aria-label','Faixa anterior em '+a.nome);
  r.next.setAttribute('aria-label','Próxima faixa em '+a.nome);
  r.mt.textContent=md.artista?md.titulo+' · '+md.artista:md.titulo;
}
function makeRow(a){
  var root=el('div','card'),top=el('div','top'),ico=el('span','ico'),name=el('span','name'),mute=el('button','mute');
  var img=new Image();img.alt='';img.width=img.height=32;img.className='ico';
  img.onerror=function(){img.replaceWith(ico);ico.textContent=(a.nome||'?').charAt(0).toUpperCase();};
  img.src='/icone/'+encodeURIComponent(a.icone);
  top.append(img,name,mute);
  var sl=el('input');sl.type='range';sl.min=0;sl.max=100;sl.step=1;
  var val=el('span','val'),line=el('div','sl');line.append(sl,val);
  var routes=el('div','routes');
  var media=el('div','media'),prev=el('button','','⏮'),pp=el('button',''),next=el('button','','⏭'),mt=el('span','mt');
  media.append(prev,pp,next,mt);media.hidden=true;
  root.append(top,line,routes,media);
  mute.onclick=function(){var cur=mute.classList.contains('muted');send({t:'mudo',app:a.chave,m:!cur});};
  prev.onclick=function(){send({t:'midia',app:a.chave,acao:'ant'});};
  pp.onclick=function(){send({t:'midia',app:a.chave,acao:'pp'});};
  next.onclick=function(){send({t:'midia',app:a.chave,acao:'prox'});};
  bindSlider(sl,a.chave,val,function(v){return{t:'volume',app:a.chave,v:v};});
  return{root:root,name:name,sl:sl,val:val,mute:mute,line:line,routes:routes,media:media,prev:prev,pp:pp,next:next,mt:mt,sig:null};
}
bindSlider($('ms'),'g',$('mv'),function(v){return{t:'geral',v:v};});
$('mm').onclick=function(){send({t:'geralMudo',m:!$('mm').classList.contains('muted')});};

