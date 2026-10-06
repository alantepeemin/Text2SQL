using Text2Sql.Domain.Authorization;
using Xunit;

namespace Text2Sql.Tests.Unit.Application
{
    /// <summary>
    /// SaaS-4 — İzin matrisi (saf birim testleri; DB/HTTP gerektirmez).
    /// Rol→izin haritası yetkilendirmenin tek doğruluk kaynağı olduğu için
    /// buradaki her assert bir güvenlik sözleşmesidir.
    /// </summary>
    public class PermissionMatrixTests
    {
        [Fact]
        public void Admin_TumIzinlereSahip()
        {
            foreach (var izin in Permissions.All)
                Assert.True(RolePermissions.Has(Roles.Admin, izin),
                    $"Admin '{izin}' iznine sahip olmalı.");
        }

        [Theory]
        [InlineData(Permissions.ProjectsCreate)]
        [InlineData(Permissions.DataSourcesManage)]
        [InlineData(Permissions.QueriesExecute)]
        [InlineData(Permissions.UsageRead)]
        [InlineData(Permissions.MembersRead)]
        public void Manager_BeklenenIzinlereSahip(string izin)
            => Assert.True(RolePermissions.Has(Roles.Manager, izin));

        [Theory]
        [InlineData(Permissions.MembersManage)]     // üye çıkarma/rol değiştirme
        [InlineData(Permissions.MembersApprove)]    // onay yetkisi
        [InlineData(Permissions.AuditRead)]         // denetim kaydı
        [InlineData(Permissions.BillingManage)]
        [InlineData(Permissions.ApiKeysManage)]
        [InlineData(Permissions.OrganizationManage)]
        [InlineData(Permissions.ProjectsManage)]
        public void Manager_YonetselIzinlereSahipDegil(string izin)
            => Assert.False(RolePermissions.Has(Roles.Manager, izin),
                $"Manager '{izin}' iznine SAHİP OLMAMALI (ayrıcalık yükseltme riski).");

        [Theory]
        [InlineData(Permissions.QueriesExecute)]
        [InlineData(Permissions.QueriesRead)]
        [InlineData(Permissions.ProjectsRead)]
        public void User_TemelIzinlereSahip(string izin)
            => Assert.True(RolePermissions.Has(Roles.User, izin));

        [Theory]
        [InlineData(Permissions.ProjectsCreate)]
        [InlineData(Permissions.DataSourcesManage)]
        [InlineData(Permissions.MembersInvite)]
        [InlineData(Permissions.UsageRead)]
        [InlineData(Permissions.AuditRead)]
        public void User_YonetselIzinlereSahipDegil(string izin)
            => Assert.False(RolePermissions.Has(Roles.User, izin));

        [Fact]
        public void BilinmeyenRol_HicbirIzneSahipDegil()
        {
            Assert.Empty(RolePermissions.For("superuser"));
            Assert.Empty(RolePermissions.For(null));
            Assert.False(RolePermissions.Has("", Permissions.QueriesExecute));
        }

        [Fact]
        public void TumRollerinIzinleri_TanimliIzinListesindeOlmali()
        {
            // Yazım hatası koruması: haritaya var olmayan bir izin adı sızmasın
            foreach (var rol in Roles.All)
                foreach (var izin in RolePermissions.For(rol))
                    Assert.Contains(izin, Permissions.All);
        }
    }
}
