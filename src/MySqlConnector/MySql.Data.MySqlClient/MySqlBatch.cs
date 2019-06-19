using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector.Core;
using MySqlConnector.Protocol.Serialization;
using MySqlConnector.Utilities;

#if NET45 || NET461 || NET471 || NETSTANDARD1_3 || NETSTANDARD2_0 || NETCOREAPP2_1
namespace System.Data.Common
{
	public abstract class DbBatch : IDisposable
	{
		public DbBatchCommandCollection BatchCommands => DbBatchCommands;
		protected abstract DbBatchCommandCollection DbBatchCommands { get; }

		#region Execution (mirrors DbCommand)

		public DbDataReader ExecuteReader() => ExecuteDbDataReader();
		protected abstract DbDataReader ExecuteDbDataReader();
		public Task<DbDataReader> ExecuteReaderAsync(CancellationToken cancellationToken = default) => ExecuteDbDataReaderAsync(cancellationToken);
		protected abstract Task<DbDataReader> ExecuteDbDataReaderAsync(CancellationToken cancellationToken);

		public abstract int ExecuteNonQuery();
		public abstract Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);

		public abstract object ExecuteScalar();
		public abstract Task<object> ExecuteScalarAsync(CancellationToken cancellationToken = default);

		#endregion

		#region Execution properties (mirrors DbCommand)

		public abstract int Timeout { get; set; }

		// Delegates to DbConnection
		public DbConnection Connection { get; set; }
		protected abstract DbConnection DbConnection { get; set; }

		// Delegates to DbTransaction
		public DbTransaction Transaction { get; set; }
		protected abstract DbTransaction DbTransaction { get; set; }

		#endregion

		#region Other methods mirroring DbCommand

		public abstract void Prepare();
		public abstract Task PrepareAsync(CancellationToken cancellationToken = default);
		public abstract void Cancel();
		public abstract Task CancelAsync(CancellationToken cancellationToken = default);

		#endregion

		#region Standard dispose pattern

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing) { }

		#endregion
	}
}
#endif

namespace MySql.Data.MySqlClient
{
	public sealed class MySqlBatch : DbBatch
	{
		public MySqlBatch()
			: this(null, null)
		{
		}

		public MySqlBatch(MySqlConnection connection = null, MySqlTransaction transaction = null)
		{
			Connection = connection;
			Transaction = transaction;
			BatchCommands = new MySqlBatchCommandCollection();
		}

		public new MySqlConnection Connection { get; set; }
		public new MySqlTransaction Transaction { get; set; }
		public new MySqlBatchCommandCollection BatchCommands { get; }

		protected override DbConnection DbConnection
		{
			get => Connection;
			set => Connection = (MySqlConnection) value;
		}

		protected override DbTransaction DbTransaction
		{
			get => Transaction;
			set => Transaction = (MySqlTransaction) value;
		}

		protected override DbBatchCommandCollection DbBatchCommands => BatchCommands;

		protected override DbDataReader ExecuteDbDataReader()
		{
			// TODO: ResetCommandTimeout();
			return ExecuteReaderAsync(IOBehavior.Synchronous, CancellationToken.None).GetAwaiter().GetResult();
		}

		protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CancellationToken cancellationToken)
		{
			// TODO: ResetCommandTimeout();
			return ExecuteReaderAsync(AsyncIOBehavior, cancellationToken);
		}

		private Task<DbDataReader> ExecuteReaderAsync(IOBehavior ioBehavior, CancellationToken cancellationToken)
		{
			if (!IsValid(out var exception))
			 	return Utility.TaskFromException<DbDataReader>(exception);

			foreach (IMySqlCommand batchCommand in BatchCommands)
				batchCommand.Connection = Connection;

			var payloadCreator = Connection.Session.SupportsComMulti ? BatchedCommandPayloadCreator.Instance : SingleCommandPayloadCreator.Instance;
			return CommandExecutor.ExecuteReaderAsync(BatchCommands, payloadCreator, default /* TODO: */, ioBehavior, cancellationToken);
		}

		public override int ExecuteNonQuery() => ExecuteNonQueryAsync(IOBehavior.Synchronous, CancellationToken.None).GetAwaiter().GetResult();

		public override object ExecuteScalar() => ExecuteScalarAsync(IOBehavior.Synchronous, CancellationToken.None).GetAwaiter().GetResult();

		public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default) => ExecuteNonQueryAsync(AsyncIOBehavior, cancellationToken);

		public override Task<object> ExecuteScalarAsync(CancellationToken cancellationToken = default) => ExecuteScalarAsync(AsyncIOBehavior, cancellationToken);

		public override int Timeout { get; set; }

		public override void Prepare() => throw new NotImplementedException();

		public override Task PrepareAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

		public override void Cancel() => throw new NotImplementedException();

		public override Task CancelAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

		protected override void Dispose(bool disposing)
		{
			try
			{
				// TODO:
				m_isDisposed = true;
			}
			finally
			{
				base.Dispose(disposing);
			}
		}

		private async Task<int> ExecuteNonQueryAsync(IOBehavior ioBehavior, CancellationToken cancellationToken)
		{
			// TODO: ResetCommandTimeout();
			using (var reader = (MySqlDataReader) await ExecuteReaderAsync(ioBehavior, cancellationToken).ConfigureAwait(false))
			{
				do
				{
					while (await reader.ReadAsync(ioBehavior, cancellationToken).ConfigureAwait(false))
					{
					}
				} while (await reader.NextResultAsync(ioBehavior, cancellationToken).ConfigureAwait(false));
				return reader.RecordsAffected;
			}
		}

		private async Task<object> ExecuteScalarAsync(IOBehavior ioBehavior, CancellationToken cancellationToken)
		{
			// TODO: ResetCommandTimeout();
			var hasSetResult = false;
			object result = null;
			using (var reader = (MySqlDataReader) await ExecuteReaderAsync(/* TODO: CommandBehavior.SingleResult | CommandBehavior.SingleRow, */ioBehavior, cancellationToken).ConfigureAwait(false))
			{
				do
				{
					var hasResult = await reader.ReadAsync(ioBehavior, cancellationToken).ConfigureAwait(false);
					if (!hasSetResult)
					{
						if (hasResult)
							result = reader.GetValue(0);
						hasSetResult = true;
					}
				} while (await reader.NextResultAsync(ioBehavior, cancellationToken).ConfigureAwait(false));
			}
			return result;
		}

		private bool IsValid(out Exception exception)
		{
			exception = null;
			if (m_isDisposed)
				exception = new ObjectDisposedException(GetType().Name);
			else if (Connection is null)
				exception = new InvalidOperationException("Connection property must be non-null.");
			else if (Connection.State != ConnectionState.Open && Connection.State != ConnectionState.Connecting)
				exception = new InvalidOperationException("Connection must be Open; current state is {0}".FormatInvariant(Connection.State));
			else if (!Connection.IgnoreCommandTransaction && Transaction != Connection.CurrentTransaction)
				exception = new InvalidOperationException("The transaction associated with this command is not the connection's active transaction; see https://fl.vu/mysql-trans");
			else if (BatchCommands.Count == 0)
				exception = new InvalidOperationException("BatchCommands must contain a command");

			if (exception is null)
			{
				foreach (var command in BatchCommands)
				{
					if ((command.CommandBehavior & CommandBehavior.CloseConnection) != 0)
					{
						exception = new NotSupportedException("CommandBehavior.CloseConnection is not supported by MySqlBatch");
						break;
					}
				}
			}

			return exception is null;
		}

		private IOBehavior AsyncIOBehavior => Connection?.AsyncIOBehavior ?? IOBehavior.Asynchronous;

		bool m_isDisposed;
	}
}
