/* ---------- brilho do monitor (Fase 12): uma barra por tela; 1 envio a cada 150 ms, valor final ao soltar ---------- */
var brRows=[],brSig='';
function renderBrilho(list){
  var sig=list.map(function(b){return b.valor==null?'x':'o';}).join('');
  var box=$('brBox');
  if(sig!==brSig||brRows.length!==list.length){
    brSig=sig;brRows=[];box.textContent='';
    list.forEach(function(b){
      var t=b.tela,name=list.length>1?'Brilho — Tela '+(t+1):'Brilho';
      if(b.valor==null){
        var w=el('p','dim','Esta tela não aceita ajuste de brilho (ative o DDC/CI no menu do monitor)'+(list.length>1?' — Tela '+(t+1):'')+'.');
        box.appendChild(w);brRows.push(null);return;
      }
      var lb=el('label','lbl',name),sl=el('input'),val=el('span','val'),line=el('div','sl'),id='br'+t;
      sl.type='range';sl.min=0;sl.max=100;sl.step=1;sl.setAttribute('aria-label',name);
      line.append(sl,val);box.append(lb,line);
      bindSlider(sl,id,val,function(v){return{t:'brilho',tela:t,valor:v};},150);
      brRows.push({sl:sl,val:val,id:id});
    });
  }
  list.forEach(function(b,i){var r=brRows[i];if(r&&b.valor!=null)setSlider(r.sl,r.val,r.id,b.valor);});
}

