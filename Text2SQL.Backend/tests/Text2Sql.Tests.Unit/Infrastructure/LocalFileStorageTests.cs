using Microsoft.Extensions.Configuration;
using Text2Sql.Infrastructure.Storage;
using Xunit;

namespace Text2Sql.Tests.Unit.Infrastructure
{
    /// <summary>FAZ 4 — yerel dosya deposu birim testleri.</summary>
    public class LocalFileStorageTests
    {
        private static LocalFileStorage Create(out string root)
        {
            root = Path.Combine(Path.GetTempPath(), $"t2s_fs_{Guid.NewGuid():N}");
            var env  = new FakeEnv(root);
            var cfg  = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                    { ["Storage:ResultsRoot"] = "results" })
                .Build();
            return new LocalFileStorage(env, cfg);
        }

        [Fact]
        public async Task KaydetVeOku_Roundtrip()
        {
            var storage = Create(out var root);
            try
            {
                var path = await storage.SaveTextAsync("1/2", "sonuc.json", "[\"a\",\"b\"]");
                Assert.Equal("1/2/sonuc.json", path);

                var content = await storage.ReadTextAsync(path);
                Assert.Equal("[\"a\",\"b\"]", content);
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task OlmayanDosya_NullDoner()
        {
            var storage = Create(out var root);
            try
            {
                Assert.Null(await storage.ReadTextAsync("yok/boyle/bir.json"));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        [Fact]
        public async Task PathTraversal_Reddedilir()
        {
            var storage = Create(out var root);
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => storage.ReadTextAsync("../../etc/passwd"));
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        private sealed class FakeEnv : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
        {
            public FakeEnv(string root) { ContentRootPath = root; }
            public string ContentRootPath { get; set; }
            public string WebRootPath { get; set; } = "";
            public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
            public string ApplicationName { get; set; } = "Tests";
            public string EnvironmentName { get; set; } = "Testing";
        }
    }
}
