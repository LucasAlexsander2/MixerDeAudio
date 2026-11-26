/* ---------- estado do PC (Fase 9d, D27): só é pedido com a aba PC aberta ---------- */
var infoOn=false, infoSub=false;
function infoSync(){
  var want=screen==='mixer'&&tab==='pc'&&infoOn;
  if(want===infoSub)return;
  infoSub=want;
  send({t:'assinar',g:'info',on:want});
}
function gb(n){return (n/1073741824).toFixed(1).replace('.',',')+' GB';}
function renderInfo(m){
  var up=m.ligado||0,d=Math.floor(up/86400),h=Math.floor(up%86400/3600),mi=Math.floor(up%3600/60);
  var mem=m.mem||{},dk=m.disco||{},b=m.bateria;
  var rows=[['CPU',(m.cpu||0)+'%'],
    ['Memória',gb(mem.usada||0)+' de '+gb(mem.total||0)],
    ['Ligado há',(d?d+' d ':'')+h+' h '+mi+' min'],
    ['Bateria',b?b.pct+'%'+(b.carregando?' (carregando)':''):'Sem bateria'],
    ['Disco do Windows',gb(dk.livre||0)+' livres de '+gb(dk.total||0)]];
  var box=$('infoBox');box.textContent='';
  rows.forEach(function(r){var d1=el('div'),a=el('span'),c=el('span');a.textContent=r[0];c.textContent=r[1];d1.appendChild(a);d1.appendChild(c);box.appendChild(d1);});
}

