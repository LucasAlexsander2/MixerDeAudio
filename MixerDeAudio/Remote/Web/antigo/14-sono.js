/* ---------- timer de sono (Fase 9c, D24) ---------- */
var slPlan=null, slTick=null;
function updSleep(){
  if(!slPlan){$('slPlan').hidden=true;clearInterval(slTick);slTick=null;return;}
  var left=Math.max(0,Math.round((slPlan.at-Date.now())/1000));
  $('slTxt').textContent='A música para em '+dur(left);
  $('slPlan').hidden=false;
}
$('slGo').onclick=function(){send({t:'sono',min:+$('slSel').value});};
$('slCancel').onclick=function(){send({t:'sono',min:0});};

