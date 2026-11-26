/* ---------- pareamento ---------- */
function pair(code){
  $('go').disabled=true;$('perr').textContent='';
  fetch('/parear',{method:'POST',body:JSON.stringify({codigo:code})}).then(function(r){
    if(r.ok){history.replaceState(null,'',location.pathname);$('code').value='';connect();}
    else{
      $('perr').textContent=r.status===401?'Código incorreto.':r.status===429?'Muitas tentativas. Aguarde 1 minuto.':'Não foi possível conectar ('+r.status+').';
      show('pair');
    }
  }).catch(function(){show('pair');$('perr').textContent='Sem conexão com o PC.';})
  .then(function(){$('go').disabled=$('code').value.length!==6;});
}
$('code').addEventListener('input',function(){
  this.value=this.value.replace(/\D/g,'').slice(0,6);
  $('go').disabled=this.value.length!==6;
});
$('code').addEventListener('keydown',function(e){if(e.key==='Enter'&&this.value.length===6)pair(this.value);});
$('go').addEventListener('click',function(){pair($('code').value);});

