using System.Security.Cryptography;
using MixerDeAudio.Remote;

namespace MixerDeAudio.Tests;

public class WebAssetsTests
{
    private const string OldPageHash = "ffff7d3dbdef971270c97731b09a759fb2b389b63149a032cc56ced9784a0d8b";

    private static string WebDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MixerDeAudio.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "MixerDeAudio", "Remote", "Web");
    }

    [Fact]
    public void Resolve_Aceita_Rotas_Validas()
    {
        var js = WebAssets.Resolve("/app/js/pilha.js");
        Assert.NotNull(js);
        Assert.Equal("MixerDeAudio.Web/app/js/pilha.js", js!.Resource);
        Assert.Equal("text/javascript; charset=utf-8", js.ContentType);
        Assert.Equal("no-cache", js.CacheControl);

        var font = WebAssets.Resolve("/fontes/x-1.woff2");
        Assert.NotNull(font);
        Assert.Equal("font/woff2", font!.ContentType);
        Assert.Contains("immutable", font.CacheControl);

        Assert.Equal("application/manifest+json", WebAssets.Resolve("/app/manifest.webmanifest")!.ContentType);
        Assert.Equal("image/png", WebAssets.Resolve("/app/icone-180.png")!.ContentType);
        Assert.Equal("text/css; charset=utf-8", WebAssets.Resolve("/app/css/tokens.css")!.ContentType);
        Assert.Equal("text/plain; charset=utf-8", WebAssets.Resolve("/fontes/OFL-x.txt".ToLowerInvariant())!.ContentType);
    }

    [Theory]
    [InlineData("/app/../x.js")]
    [InlineData("/app//x.js")]
    [InlineData("/app/%2e%2e/x.js")]
    [InlineData("/app/x\\y.js")]
    [InlineData("/app/X.JS")]
    [InlineData("/app/x.exe")]
    [InlineData("/app/")]
    [InlineData("/antigo/04-base.js")]
    [InlineData("/app/antigo/04-base.js.exe")]
    [InlineData("/fontes")]
    [InlineData("/fontes/")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_Recusa_Caminhos_Invalidos(string? path) => Assert.Null(WebAssets.Resolve(path));

    [Fact]
    public void ETagFor_Formato_E_Estabilidade()
    {
        var a = WebAssets.ETagFor(new byte[] { 1, 2, 3 });
        Assert.Equal(a, WebAssets.ETagFor(new byte[] { 1, 2, 3 }));
        Assert.NotEqual(a, WebAssets.ETagFor(new byte[] { 1, 2, 4 }));
        Assert.Matches("^\"[0-9a-f]{16}\"$", a);
    }

    [Fact]
    public void IfNoneMatchMatches_Trata_Lista_Fraco_E_Curinga()
    {
        const string e = "\"0123456789abcdef\"";
        Assert.True(WebAssets.IfNoneMatchMatches(e, e));
        Assert.True(WebAssets.IfNoneMatchMatches("\"aaa\" ,  " + e + " , \"bbb\"", e));
        Assert.True(WebAssets.IfNoneMatchMatches("W/" + e, e));
        Assert.True(WebAssets.IfNoneMatchMatches("*", e));
        Assert.False(WebAssets.IfNoneMatchMatches(null, e));
        Assert.False(WebAssets.IfNoneMatchMatches("", e));
        Assert.False(WebAssets.IfNoneMatchMatches("\"outro\"", e));
    }

    [Fact]
    public void PaginaAntiga_Partes_Concatenadas_Tem_O_Hash_Congelado()
    {
        var dir = Path.Combine(WebDir(), "antigo");
        using var ms = new MemoryStream();
        foreach (var part in WebAssets.OldPageParts)
        {
            var bytes = File.ReadAllBytes(Path.Combine(dir, part));
            ms.Write(bytes, 0, bytes.Length);
        }
        var semCr = ms.ToArray().Where(b => b != 13).ToArray();
        Assert.Equal(OldPageHash, Convert.ToHexString(SHA256.HashData(semCr)).ToLowerInvariant());
    }

    [Fact]
    public void PaginaAntiga_Lista_Igual_A_Pasta()
    {
        var files = Directory.GetFiles(Path.Combine(WebDir(), "antigo")).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(WebAssets.OldPageParts.OrderBy(x => x, StringComparer.Ordinal).ToArray(), files);
    }

    [Fact]
    public void Arquivos_Do_Disco_Passam_No_Resolve_E_Fontes_Estao_Corretas()
    {
        var web = WebDir();
        foreach (var sub in new[] { "app", "fontes" })
        {
            var root = Path.Combine(web, sub);
            if (!Directory.Exists(root)) continue;
            foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                // Licencas e LEIAME (nomes com maiuscula, D17) vao no .exe mas nao sao rotas (D2: so [a-z0-9._/-])
                if (sub == "fontes" && !f.EndsWith(".woff2", StringComparison.Ordinal)) continue;
                if (f.EndsWith(".md", StringComparison.Ordinal)) continue; // documentacao dos componentes (14c): nao e servida nem embutida
                var url = "/" + sub + "/" + Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/');
                Assert.True(WebAssets.Resolve(url) is not null, "Resolve recusou " + url);
            }
        }
        var fontes = Path.Combine(web, "fontes");
        foreach (var woff in Directory.GetFiles(fontes, "*.woff2"))
        {
            var head = new byte[4];
            using (var fs = File.OpenRead(woff)) fs.ReadExactly(head);
            Assert.Equal("wOF2", System.Text.Encoding.ASCII.GetString(head));
        }
        Assert.True(File.Exists(Path.Combine(fontes, "OFL-Manrope.txt")));
        Assert.True(File.Exists(Path.Combine(fontes, "OFL-JetBrainsMono.txt")));
        Assert.True(File.Exists(Path.Combine(fontes, "manrope-latin-1.woff2")));
        Assert.True(File.Exists(Path.Combine(fontes, "jetbrainsmono-500-latin-1.woff2")));
    }
}
