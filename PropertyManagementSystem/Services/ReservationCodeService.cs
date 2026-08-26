using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public interface IReservationCodeService
    {
        Task CreateAsync(
            GFMS_ReservationHeader reservation,
            string prefix,
            CancellationToken cancellationToken = default);
    }

    public sealed class ReservationCodeService : IReservationCodeService
    {
        private readonly DAL _context;

        public ReservationCodeService(DAL context)
        {
            _context = context;
        }

        public async Task CreateAsync(
            GFMS_ReservationHeader reservation,
            string prefix,
            CancellationToken cancellationToken = default)
        {
            if (!_context.Database.IsRelational())
            {
                reservation.ReservationCode = $"{prefix}-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}";
                _context.ReservationHeaders.Add(reservation);
                await _context.SaveChangesAsync(cancellationToken);
                return;
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            var year = DateTime.UtcNow.Year;
            var resource = $"GFMS:ReservationCode:{prefix}:{year}";

            await _context.Database.ExecuteSqlRawAsync(
                "EXEC sys.sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', " +
                "@LockOwner = 'Transaction', @LockTimeout = 10000;",
                new object[] { resource }, cancellationToken);

            var connection = _context.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = @"
SELECT ISNULL(MAX(TRY_CONVERT(int, RIGHT(ReservationCode, 4))), 0)
FROM dbo.GFMS_ReservationHeader WITH (UPDLOCK, HOLDLOCK)
WHERE ReservationType = @reservationType
  AND ReservationCode LIKE @codePattern;";
            command.Parameters.Add(new SqlParameter("@reservationType", reservation.ReservationType));
            command.Parameters.Add(new SqlParameter("@codePattern", $"{prefix}-{year}-%"));
            var lastValue = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));

            reservation.ReservationCode = $"{prefix}-{year}-{lastValue + 1:D4}";
            _context.ReservationHeaders.Add(reservation);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
