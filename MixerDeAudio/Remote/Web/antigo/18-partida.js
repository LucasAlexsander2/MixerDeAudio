/* ---------- partida ---------- */
var m=/^#c=(\d{6})$/.exec(location.hash);
if(m){show('pair');$('code').value=m[1];pair(m[1]);}
else{banner(true,'Conectando…');connect();}
})();
</script>
</body>
</html>
