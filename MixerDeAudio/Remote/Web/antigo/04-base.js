(function(){
'use strict';
var $=function(i){return document.getElementById(i)};
var G={'':'🎧','':'🔊','':'🖥️'};
var S=null, ws=null, opened=false, att=0, timer=null, screen='', hold={}, last={}, pend={}, rows={}, sig={};

var tab='som', tabsOn=false, gone=false, busyOn=false;
function show(which){
  screen=which;
  $('pair').hidden=which!=='pair';
  $('busy').hidden=which!=='busy';
  $('gone').hidden=which!=='gone';
  view();
  if(which==='pair'||which==='busy'||which==='gone'){$('banner').hidden=true;}
}
/* abas: só aparecem com algum grupo de ferramentas ligado */
function view(){
  var m=screen==='mixer';
  $('mixer').hidden=!(m&&tab==='som');
  $('pc').hidden=!(m&&tab==='pc');
  $('ctl').hidden=!(m&&tab==='ctl');
  $('tl').hidden=!(m&&tab==='tl');
  $('jn').hidden=!(m&&tab==='jn');
  $('tf').hidden=!(m&&tab==='tf');
  winSync();infoSync();
  var fo=m&&tabsOn;$('tabs').hidden=!fo;$('fab').hidden=!fo;
  if(!fo)fabClose();else fabPlace();
  /* a tela só é transmitida com a aba aberta (D18) */
  if(!(m&&tab==='tl'))tlStop();
  if(!(m&&tab==='ctl'))padReset();
}
function toast(t){var e=$('toast');e.textContent=t;e.hidden=false;clearTimeout(toast.t);toast.t=setTimeout(function(){e.hidden=true;},4000);}
function banner(on,text){var b=$('banner');b.hidden=!on;if(text)b.textContent=text;}

