/* ---------- sliders: 1 mensagem a cada 50 ms, valor final ao soltar, ignora o estado por 500 ms ---------- */
function bindSlider(el,id,out,mk,gap){
  gap=gap||50;
  function emit(final){
    var v=+el.value;out.textContent=v;
    pend[id]=v;
    var now=Date.now(),wait=gap-(now-(last[id]||0));
    if(final||wait<=0){flush();}
    else if(!pend[id+'#']){pend[id+'#']=setTimeout(flush,wait);}
    function flush(){clearTimeout(pend[id+'#']);pend[id+'#']=0;last[id]=Date.now();if(pend[id]!=null){send(mk(pend[id]));pend[id]=null;}}
  }
  function release(){hold[id]=Date.now()+500;setTimeout(function(){if(S)render(S);},520);}
  el.addEventListener('pointerdown',function(){hold[id]=Infinity;});
  el.addEventListener('input',function(){if(hold[id]!==Infinity)hold[id]=Date.now()+500;emit(false);});
  el.addEventListener('change',function(){emit(true);release();});
  el.addEventListener('pointerup',release);
  el.addEventListener('pointercancel',release);
}
function held(id){return hold[id]!==undefined&&Date.now()<hold[id];}
function setSlider(el,out,id,v){if(held(id))return;if(+el.value!==v)el.value=v;out.textContent=v;}

