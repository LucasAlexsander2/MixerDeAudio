using System.Text.Json;
using System.Text.RegularExpressions;

namespace MixerDeAudio.Tests;

// Fase 14b: confere os arquivos do novo front (Remote/Web/app) direto do repositório, sem navegador.
public class WebFrontTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MixerDeAudio.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "MixerDeAudio", "Remote", "Web", "app");
    }

    private static IEnumerable<string> Files(params string[] extensions) =>
        Directory.EnumerateFiles(AppDir(), "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.Ordinal));

    private static string Rel(string path) => Path.GetRelativePath(AppDir(), path).Replace('\\', '/');

    private static string AppFile(string urlPath) // "/app/js/x.js" -> caminho em disco
    {
        Assert.StartsWith("/app/", urlPath);
        return Path.Combine(AppDir(), urlPath["/app/".Length..].Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FontFile(string urlPath)
    {
        Assert.StartsWith("/fontes/", urlPath);
        return Path.Combine(AppDir(), "..", "fontes", urlPath["/fontes/".Length..]);
    }

    private static bool ExistsForUrl(string url) =>
        url.StartsWith("/app/", StringComparison.Ordinal) ? File.Exists(AppFile(url))
        : url.StartsWith("/fontes/", StringComparison.Ordinal) && File.Exists(FontFile(url));

    [Fact]
    public void Modulos_ImportamSoArquivosQueExistem()
    {
        var import = new Regex(@"(?:import|export)\s[^;'""]*?from\s+['""](?<p>[^'""]+)['""]|import\s+['""](?<p>[^'""]+)['""]");
        var modules = Files(".js").ToList();
        Assert.NotEmpty(modules);
        var missing = new List<string>();
        foreach (var file in modules)
        {
            foreach (Match m in import.Matches(File.ReadAllText(file)))
            {
                var spec = m.Groups["p"].Value;
                if (!spec.StartsWith("./", StringComparison.Ordinal) && !spec.StartsWith("../", StringComparison.Ordinal))
                {
                    missing.Add($"{Rel(file)}: import fora do app ({spec})");
                    continue;
                }
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, spec));
                if (!File.Exists(target)) missing.Add($"{Rel(file)}: {spec}");
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void IndexHtml_ReferenciasAbsolutasApontamParaArquivosQueExistem()
    {
        var html = File.ReadAllText(Path.Combine(AppDir(), "index.html"));
        var refs = Regex.Matches(html, @"\b(?:src|href)=""(?<p>/[^""]*)""").Select(m => m.Groups["p"].Value).ToList();
        Assert.Contains("/app/js/app.js", refs);
        Assert.Contains("/app/manifest.webmanifest", refs);
        Assert.Contains("/app/css/tokens.css", refs);
        Assert.Contains("/app/icone-180.png", refs);
        Assert.Contains("/fontes/manrope-latin-1.woff2", refs);
        Assert.All(refs, r => Assert.True(ExistsForUrl(r), "não existe: " + r));
        Assert.Contains("type=\"module\"", html);
        Assert.Contains("viewport-fit=cover", html);
        Assert.Contains("apple-mobile-web-app-capable", html);
        Assert.Contains("black-translucent", html);
        Assert.Contains("color-scheme", html);
    }

    [Fact]
    public void Css_UrlsApontamParaArquivosQueExistem()
    {
        var urls = new List<string>();
        foreach (var css in Files(".css"))
            urls.AddRange(Regex.Matches(File.ReadAllText(css), @"url\(\s*[""']?(?<p>[^""')\s]+)").Select(m => m.Groups["p"].Value));
        Assert.NotEmpty(urls);
        Assert.All(urls, u => Assert.True(ExistsForUrl(u), "não existe: " + u));
    }

    [Fact]
    public void Manifesto_TemOsCamposDaD15_ECadaIconeTemOTamanhoDeclarado()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppDir(), "manifest.webmanifest")));
        var root = doc.RootElement;
        Assert.Equal("Controladora PC", root.GetProperty("name").GetString());
        Assert.Equal("Controladora", root.GetProperty("short_name").GetString());
        Assert.Equal("/novo", root.GetProperty("start_url").GetString());
        Assert.Equal("/", root.GetProperty("scope").GetString());
        Assert.Equal("standalone", root.GetProperty("display").GetString());
        Assert.Equal("#0f1218", root.GetProperty("background_color").GetString());
        Assert.Equal("#0f1218", root.GetProperty("theme_color").GetString());

        var icons = root.GetProperty("icons").EnumerateArray().ToList();
        Assert.Equal(new[] { 192, 512 }, icons.Select(i => int.Parse(i.GetProperty("sizes").GetString()!.Split('x')[0])).Order().ToArray());
        foreach (var icon in icons)
        {
            Assert.Equal("any maskable", icon.GetProperty("purpose").GetString());
            var src = icon.GetProperty("src").GetString()!;
            Assert.True(ExistsForUrl(src), "não existe: " + src);
            var declared = icon.GetProperty("sizes").GetString()!.Split('x');
            var (w, h) = PngSize(AppFile(src));
            Assert.Equal(int.Parse(declared[0]), w);
            Assert.Equal(int.Parse(declared[1]), h);
        }
        var (w180, h180) = PngSize(Path.Combine(AppDir(), "icone-180.png"));
        Assert.Equal((180, 180), (w180, h180));
    }

    private static (int W, int H) PngSize(string path)
    {
        var b = File.ReadAllBytes(path);
        Assert.True(b.Length > 24, path);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, b[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(b, 12, 4));
        int Be(int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        return (Be(16), Be(20));
    }

    [Fact]
    public void Tokens_TemOs13DaTabela4_1ComOsValoresDoGuia()
    {
        var css = File.ReadAllText(Path.Combine(AppDir(), "css", "tokens.css"));
        var esperado = new Dictionary<string, string>
        {
            ["--bg"] = "#0f1218", ["--surface"] = "#171b23", ["--surface-2"] = "#1f2430", ["--line"] = "#2a3040",
            ["--fg"] = "#e8ebf2", ["--muted"] = "#8b93a7", ["--accent"] = "#8fb4ff", ["--accent-ink"] = "#0f1218",
            ["--danger"] = "#ff7a7a", ["--ok"] = "#6fd3a0", ["--scrim"] = "rgb(5 7 10 / .62)",
            ["--glass"] = "rgb(23 27 35 / .7)", ["--sel"] = "rgb(143 180 255 / .25)",
        };
        Assert.Equal(13, esperado.Count);
        foreach (var (nome, valor) in esperado)
        {
            var m = Regex.Match(css, Regex.Escape(nome) + @"\s*:\s*(?<v>[^;]+);");
            Assert.True(m.Success, "falta o token " + nome);
            Assert.Equal(valor, m.Groups["v"].Value.Trim());
        }
    }

    [Fact]
    public void CoresFixas_SoExistemEmTokensCss()
    {
        // D20: nos CSS e JS só por tokens. O HTML (meta theme-color) e o manifesto precisam do valor literal por definição.
        var cor = new Regex(@"#[0-9a-fA-F]{3}(?:[0-9a-fA-F]{3})?\b|\brgba?\(", RegexOptions.IgnoreCase);
        var culpados = Files(".css", ".js")
            .Where(f => Rel(f) != "css/tokens.css")
            .Where(f => cor.IsMatch(File.ReadAllText(f)))
            .Select(Rel).ToList();
        Assert.Empty(culpados);
    }

    [Fact]
    public void App_NaoCitaEnderecosDaInternet()
    {
        // D18: nenhum http:// ou https:// em app/ (o xmlns do SVG é a única exceção permitida)
        var proibidos = new List<string>();
        foreach (var file in Files(".html", ".js", ".css", ".webmanifest"))
        {
            var texto = File.ReadAllText(file).Replace("http://www.w3.org/2000/svg", "");
            if (Regex.IsMatch(texto, @"https?://", RegexOptions.IgnoreCase)) proibidos.Add(Rel(file));
        }
        Assert.Empty(proibidos);
    }

    [Fact]
    public void ComponentesDaD20_TemUmArquivoEUmaFuncaoCriar()
    {
        foreach (var (arquivo, funcao) in new[]
        {
            ("cabecalho", "criarCabecalho"), ("bloco", "criarBloco"), ("botao", "criarBotao"),
            ("aviso", "criarAviso"), ("toast", "criarToast"), ("dica", "criarDica"),
        })
        {
            var path = Path.Combine(AppDir(), "js", "componentes", arquivo + ".js");
            Assert.True(File.Exists(path), arquivo);
            Assert.Contains("export function " + funcao + "(", File.ReadAllText(path));
        }
    }

    // ---------- Fase 14c: componentes compartilhados ----------

    private static readonly (string Arquivo, string[] Funcoes)[] ComponentesD23 =
    {
        ("cabecalho", new[] { "criarCabecalho" }), ("bloco", new[] { "criarBloco" }), ("faixa", new[] { "criarFaixa", "criarGrade" }),
        ("botao", new[] { "criarBotao" }), ("botaogrande", new[] { "criarBotaoGrande" }), ("cartao", new[] { "criarCartao" }),
        ("cartaoapp", new[] { "criarCartaoApp" }), ("barra", new[] { "criarBarra" }), ("chip", new[] { "criarChip", "criarGrupoChips" }),
        ("interruptor", new[] { "criarInterruptor" }), ("medidor", new[] { "criarMedidor" }), ("aviso", new[] { "criarAviso" }),
        ("folha", new[] { "criarFolha" }), ("toast", new[] { "criarToast" }), ("dica", new[] { "criarDica" }), ("touchpad", new[] { "criarTouchpad" }),
    };

    private static string Componentes() => Path.Combine(AppDir(), "js", "componentes");

    [Fact]
    public void Componentes_D23_TemArquivoFuncaoCriarEEstaoNoLeiame()
    {
        var leiame = File.ReadAllText(Path.Combine(Componentes(), "LEIAME.md"));
        foreach (var (arquivo, funcoes) in ComponentesD23)
        {
            var path = Path.Combine(Componentes(), arquivo + ".js");
            Assert.True(File.Exists(path), arquivo);
            var js = File.ReadAllText(path);
            Assert.Contains("## " + arquivo + ".js", leiame);
            foreach (var f in funcoes)
            {
                Assert.Contains("export function " + f + "(", js);
                Assert.Contains("`" + f, leiame);
            }
        }
    }

    [Fact]
    public void Componentes_TodoExportAparece_NoLeiame_ENaoHaArquivoForaDaLista()
    {
        var leiame = File.ReadAllText(Path.Combine(Componentes(), "LEIAME.md"));
        var export = new Regex(@"^export\s+(?:async\s+)?(?:function|const|class|let)\s+(?<n>\w+)", RegexOptions.Multiline);
        var arquivos = Directory.GetFiles(Componentes(), "*.js").Select(f => Path.GetFileNameWithoutExtension(f)).Order().ToArray();
        Assert.Equal(ComponentesD23.Select(c => c.Arquivo).Order().ToArray(), arquivos);
        foreach (var file in Directory.GetFiles(Componentes(), "*.js"))
            foreach (Match m in export.Matches(File.ReadAllText(file)))
                Assert.True(leiame.Contains(m.Groups["n"].Value, StringComparison.Ordinal), Rel(file) + ": " + m.Groups["n"].Value + " nao esta no LEIAME.md");
    }

    [Fact]
    public void Componentes_NaoDependemDeConexaoEstadoNemIdsGlobais()
    {
        foreach (var file in Directory.GetFiles(Componentes(), "*.js"))
        {
            var js = File.ReadAllText(file);
            Assert.DoesNotMatch(@"from\s+['""]\.\./(conexao|estado|pilha)\.js['""]", js);
            Assert.DoesNotContain("getElementById", js);
            Assert.DoesNotContain("WebSocket", js);
        }
    }

    [Fact]
    public void Componentes_BarraFaixaETouchpad_VemMarcadosComDataSemGesto()
    {
        foreach (var arquivo in new[] { "barra", "faixa", "touchpad" })
            Assert.Contains("data-sem-gesto", File.ReadAllText(Path.Combine(Componentes(), arquivo + ".js")));
    }

    [Fact]
    public void Icones_CitadosNosComponentesETelas_ExistemEmIconesJs()
    {
        var existentes = Regex.Matches(File.ReadAllText(Path.Combine(AppDir(), "js", "icones.js")), @"^\s{2}(?<n>[a-z0-9]+):'", RegexOptions.Multiline)
            .Select(m => m.Groups["n"].Value).ToHashSet();
        Assert.True(existentes.Count >= 43, "icones.js tem " + existentes.Count);
        var literais = new[]
        {
            new Regex(@"\bicone\(\s*'(?<n>[a-z0-9]+)'"), new Regex(@"\bicone:\s*'(?<n>[a-z0-9]+)'"),
            new Regex(@"\|\|\s*'(?<n>[a-z0-9]+)'\)"), new Regex(@"\bicone\([^)]*\?\s*'(?<a>[a-z0-9]+)'\s*:\s*'(?<n>[a-z0-9]+)'"),
        };
        var citados = new HashSet<string>();
        var arquivos = Directory.GetFiles(Componentes(), "*.js").Concat(Directory.GetFiles(Path.Combine(AppDir(), "js", "telas"), "*.js"));
        foreach (var file in arquivos)
        {
            var js = File.ReadAllText(file);
            foreach (var r in literais)
                foreach (Match m in r.Matches(js))
                {
                    citados.Add(m.Groups["n"].Value);
                    if (m.Groups["a"].Success) citados.Add(m.Groups["a"].Value);
                }
            var lista = Regex.Match(js, @"const icones = \[(?<l>[^\]]*)\]");
            if (lista.Success) foreach (Match m in Regex.Matches(lista.Groups["l"].Value, @"'([a-z0-9]+)'")) citados.Add(m.Groups[1].Value);
        }
        Assert.True(citados.Count >= 20, "poucos icones citados: " + citados.Count);
        Assert.All(citados, c => Assert.True(existentes.Contains(c), "icone inexistente: " + c));
    }

    [Fact]
    public void Galeria_UsaTodosOsComponentes_EAbreSemOPc()
    {
        var galeria = File.ReadAllText(Path.Combine(AppDir(), "js", "telas", "galeria.js"));
        // toast e dica chegam prontos de app.js; o resto a galeria cria
        foreach (var (_, funcoes) in ComponentesD23)
            foreach (var f in funcoes.Where(f => f != "criarToast" && f != "criarDica"))
                Assert.Contains(f + "(", galeria);
        var app = File.ReadAllText(Path.Combine(AppDir(), "js", "app.js"));
        Assert.Contains("'#galeria'", app);
        Assert.Contains("registrar('galeria'", app);
        Assert.Matches(@"else iniciarConexao\(", app); // no modo galeria a conexao nem comeca
    }

    [Fact]
    public void MaoEsquerda_PreferenciasECssEOpcoesRegistradas()
    {
        var pref = File.ReadAllText(Path.Combine(AppDir(), "js", "preferencias.js"));
        foreach (var f in new[] { "lerMao", "definirMao", "aoMudarMao" })
            Assert.Matches(@"export function " + f + @"\(", pref);
        var css = File.ReadAllText(Path.Combine(AppDir(), "css", "componentes.css"));
        Assert.Matches(@"\.mao-esquerda[^{]*\.cartao-app", css);
        Assert.Matches(@"\.mao-esquerda[^{]*\.tp-", css);
        Assert.True(File.Exists(Path.Combine(AppDir(), "js", "telas", "opcoes.js")));
        var app = File.ReadAllText(Path.Combine(AppDir(), "js", "app.js"));
        Assert.Contains("registrar('opcoes'", app);
        Assert.Contains("lerMao()", app);
    }
}
