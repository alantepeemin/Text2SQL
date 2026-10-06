using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;   // HttpMethodAttribute burada tanımlı
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Text2Sql.Api.Attributes;
using Text2Sql.Api.Auth;
using Text2Sql.Infrastructure.Persistence;
using Xunit;

namespace Text2Sql.Tests.Architecture
{
    /// <summary>
    /// SaaS-8 — Yetkilendirme kapsamının MİMARİ garantisi.
    ///
    /// SaaS-4'te sınıf düzeyi [Authorize(Roles="admin")] kaldırılmıştı; o gün
    /// 11 endpoint'in 11'inde izin olduğunu ELLE doğrulamıştım. O kontrol
    /// artık otomatik: korumasız bir yönetsel uç eklenirse CI kırmızıya döner.
    /// </summary>
    public class AuthorizationCoverageTests
    {
        /// <summary>
        /// Kimlik doğrulaması gerektirmeyen (bilinçli anonim) uçlar.
        /// Yeni bir uç buraya eklenecekse gerekçesi PR'da tartışılmalıdır.
        /// </summary>
        private static readonly HashSet<string> BilincliAnonimUclar = new()
        {
            "AuthController.CreateCompany",
            "AuthController.CompleteRegistration",
            "AuthController.Login",
            "AuthController.AcceptInvitation",
            "AuthController.JoinByCode",
            "AuthController.Refresh",
            "AuthController.ConfirmEmail",
        };

        /// <summary>
        /// İzin gerektirmeyen ama kimlik doğrulaması gerektiren uçlar:
        /// kullanıcının KENDİ kaynağı üzerinde çalışanlar veya proje düzeyi
        /// izinle (RequireProjectPermission) korunanlar.
        /// </summary>
        private static readonly HashSet<string> SelfServisVeyaProjeIzniyleKorunan = new()
        {
            "AuthController.Logout",
            "AuthController.SwitchOrganization",
            "AuthController.ResendEmailConfirmation",
            "UserController.GetProfile",
            "UserController.UpdateProfile",
            "UserController.ChangePassword",
            "UserController.DeactivateAccount",
            "UserController.GetMyOrganizations",
            "ProjectController.GetMyProjects",
            "BillingController.GetPlans",
            "BillingController.GetSubscription",
        };

        private static IEnumerable<(string Ad, MethodInfo Metod, Type Controller)> TumUclar()
        {
            var apiAssembly = typeof(Text2Sql.Api.Controllers.AuthController).Assembly;

            foreach (var controller in apiAssembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
            {
                foreach (var metod in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.DeclaringType == controller)
                    .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()))
                {
                    yield return ($"{controller.Name}.{metod.Name}", metod, controller);
                }
            }
        }

        [Fact]
        public void HerYonetselUc_IzinVeyaProjeIzniyleKorunmali()
        {
            var korumasiz = new List<string>();

            foreach (var (ad, metod, controller) in TumUclar())
            {
                if (BilincliAnonimUclar.Contains(ad)) continue;
                if (SelfServisVeyaProjeIzniyleKorunan.Contains(ad)) continue;

                var izinVar = metod.GetCustomAttributes<RequirePermissionAttribute>().Any();
                var projeIzniVar = metod.GetCustomAttributes<RequireProjectPermissionAttribute>().Any()
                                   || controller.GetCustomAttributes<RequireProjectPermissionAttribute>().Any();

                if (!izinVar && !projeIzniVar)
                    korumasiz.Add(ad);
            }

            Assert.True(korumasiz.Count == 0,
                "İzin kontrolü olmayan uçlar (RequirePermission veya RequireProjectPermission ekleyin, " +
                "ya da bilinçliyse testteki beyaz listeye gerekçesiyle alın): " +
                string.Join(", ", korumasiz));
        }

        [Fact]
        public void AnonimUclar_AcikcaAllowAnonymousIsaretli()
        {
            var eksik = new List<string>();

            foreach (var (ad, metod, _) in TumUclar())
            {
                if (!BilincliAnonimUclar.Contains(ad)) continue;

                if (!metod.GetCustomAttributes<AllowAnonymousAttribute>().Any())
                    eksik.Add(ad);
            }

            Assert.True(eksik.Count == 0,
                "Anonim olduğu varsayılan uçlarda [AllowAnonymous] yok: " + string.Join(", ", eksik));
        }
    }
}
