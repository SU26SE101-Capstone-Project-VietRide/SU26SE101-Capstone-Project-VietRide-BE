using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VietRide.Payment.Infrastructure.Migrations;

namespace VietRide.Payment.UnitTests.Infrastructure;

public sealed class PaymentBusinessCodeMigrationTests
{
    [Fact]
    public void Up_AddsNullableColumnsAndExpectedIndexes()
    {
        var operations = BuildOperations("Up");

        operations.OfType<AddColumnOperation>().Should().OnlyContain(operation =>
            operation.Schema == "vietride_payment"
            && operation.IsNullable
            && operation.MaxLength == 30);
        operations.OfType<AddColumnOperation>().Select(operation => (operation.Table, operation.Name)).Should().Equal(
            ("platform_wallet_transactions", "transaction_code"),
            ("operator_wallet_transactions", "transaction_code"),
            ("operator_trip_settlements", "settlement_code"),
            ("operator_trip_settlements", "trip_code"));

        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        indexes.Select(operation => operation.Name).Should().Equal(
            "uq_platform_wallet_transactions_code",
            "uq_operator_wallet_transactions_code",
            "idx_operator_trip_settlements_trip_code",
            "uq_operator_trip_settlements_code");
        indexes.Where(operation => operation.IsUnique).Should().OnlyContain(operation =>
            operation.Filter == "transaction_code IS NOT NULL"
            || operation.Filter == "settlement_code IS NOT NULL");
    }

    [Fact]
    public void Down_DropsFourIndexesBeforeFourColumns()
    {
        var operations = BuildOperations("Down");

        operations.Take(4).Should().OnlyContain(operation => operation is DropIndexOperation);
        operations.Skip(4).Should().OnlyContain(operation => operation is DropColumnOperation);
        operations.OfType<DropIndexOperation>().Select(operation => operation.Name).Should().Equal(
            "uq_platform_wallet_transactions_code",
            "uq_operator_wallet_transactions_code",
            "idx_operator_trip_settlements_trip_code",
            "uq_operator_trip_settlements_code");
        operations.OfType<DropColumnOperation>().Select(operation => (operation.Table, operation.Name)).Should().Equal(
            ("platform_wallet_transactions", "transaction_code"),
            ("operator_wallet_transactions", "transaction_code"),
            ("operator_trip_settlements", "settlement_code"),
            ("operator_trip_settlements", "trip_code"));
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(Migration).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddPaymentBusinessCodesReleaseA(), [builder]);
        return builder.Operations;
    }
}
