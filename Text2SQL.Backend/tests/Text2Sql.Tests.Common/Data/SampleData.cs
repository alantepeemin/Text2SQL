using Microsoft.Data.Sqlite;

namespace Text2Sql.Tests.Common.Data
{
    /// <summary>Testlerin kullandığı örnek veri kaynakları.</summary>
    public static class SampleData
    {
        /// <summary>Örnek veritabanındaki satır sayısı — testler bunu referans alır.</summary>
        public const int CityCount = 3;

        /// <summary>Diskte gerçek bir SQLite dosyası üretir (cities tablosu, 3 satır).</summary>
        public static byte[] SqliteBytes()
        {
            var path = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}.sqlite");

            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE cities (
                        id INTEGER PRIMARY KEY,
                        name TEXT NOT NULL,
                        population INTEGER NOT NULL
                    );
                    INSERT INTO cities (name, population) VALUES
                        ('Istanbul', 15900000),
                        ('Ankara',    5800000),
                        ('Izmir',     4500000);
                    """;
                cmd.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();   // dosya kilidini bırak
            var bytes = File.ReadAllBytes(path);
            try { File.Delete(path); } catch { }
            return bytes;
        }

        /// <summary>.db uzantılı ama SQLite olmayan içerik — magic byte kontrolü için.</summary>
        public static byte[] NotASqliteFile()
            => "Bu bir SQLite dosyasi degil, duz metin."u8.ToArray();

        /// <summary>Benzersiz e-posta — Email alanı benzersiz indekslidir.</summary>
        public static string UniqueEmail(string onek = "admin")
            => $"{onek}-{Guid.NewGuid():N}@test.local";
    }
}
