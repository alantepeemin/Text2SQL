using Text2Sql.Domain.Common;
using Text2Sql.Domain.Enums;

namespace Text2Sql.Domain.Entities
{
    public class ProjectAccess : ITenantScoped
    {
        public int Id { get; set; }

        // SaaS-1: Kiracı filtresi için denormalize edildi. Project üzerinden
        // navigasyonla filtrelemek (a) EF query filter'da güvenilir değildir,
        // (b) her sorguya JOIN ekler. Tek kolon, tek indeks — doğru takas.
        public int CompanyId { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int ProjectId { get; set; }
        public Project Project { get; set; } = null!;

        public ProjectPermission Permission { get; set; } = ProjectPermission.Viewer;
        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

        // Kim verdi bu erişimi?
        public int? GrantedById { get; set; }
        public User? GrantedBy { get; set; }
    }
}
