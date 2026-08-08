using Microsoft.EntityFrameworkCore;

namespace Pirouette.Infrastructure;

public class PirouetteDbContext(DbContextOptions<PirouetteDbContext> options) : DbContext(options)
{
}
