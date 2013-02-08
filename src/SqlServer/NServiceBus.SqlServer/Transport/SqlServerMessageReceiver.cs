namespace NServiceBus.SQLServer.Transport
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.SqlClient;
    using System.Threading;
    using Logging;
    using Serializers.Json;
    using Unicast.Queuing;

    public class SqlServerMessageReceiver : IReceiveMessages
    {
        public string ConnectionString { get; set; }
        public bool PurgeOnStartup { get; set; }
        public int SleepTimeBetweenPolls { get; set; }


        public void Init(Address address, bool transactional)
        {
            tableName = address.Queue;

            if (PurgeOnStartup)
            {
                using (var connection = new SqlConnection(ConnectionString))
                {
                    var sql = string.Format(SqlPurge, tableName);
                    connection.Open();
                    using (var command = new SqlCommand(sql, connection) {CommandType = CommandType.Text})
                    {
                        var numberOfPurgedRows = command.ExecuteNonQuery();

                        Logger.InfoFormat("{0} messages was purged from table {1}", numberOfPurgedRows, tableName);
                    }
                }
            }
        }

        public TransportMessage Receive()
        {
            using (var connection = new SqlConnection(ConnectionString))
            {
                var sql = string.Format(SqlReceive, tableName);
                connection.Open();
                using (var command = new SqlCommand(sql, connection) {CommandType = CommandType.Text})
                {
                    using (var dataReader = command.ExecuteReader(CommandBehavior.SingleRow))
                    {
                        if (dataReader.Read())
                        {
                            var id = dataReader.GetGuid(0).ToString();

                            var correlationId = dataReader.IsDBNull(1) ? null : dataReader.GetString(1);
                            var replyToAddress = Address.Parse(dataReader.GetString(2));
                            var recoverable = dataReader.GetBoolean(3);

                            MessageIntentEnum messageIntent;
                            Enum.TryParse(dataReader.GetString(4), out messageIntent);

                            var timeToBeReceived = TimeSpan.FromTicks(dataReader.GetInt64(5));
                            var headers =
                                Serializer.DeserializeObject<Dictionary<string, string>>(dataReader.GetString(6));
                            byte[] body = dataReader.GetSqlBinary(7).Value;

                            var message = new TransportMessage
                                {
                                    Id = id,
                                    CorrelationId = correlationId,
                                    ReplyToAddress = replyToAddress,
                                    Recoverable = recoverable,
                                    MessageIntent = messageIntent,
                                    TimeToBeReceived = timeToBeReceived,
                                    Headers = headers,
                                    Body = body
                                };

                            return message;
                        }
                    }
                }
            }
            if (SleepTimeBetweenPolls > 0)
                Thread.Sleep(SleepTimeBetweenPolls);
            else
                Thread.Sleep(1000);
            return null;
        }


        string tableName;
        static readonly JsonMessageSerializer Serializer = new JsonMessageSerializer(null);

        const string SqlReceive =
            @"WITH message AS (SELECT TOP(1) * FROM [{0}] WITH (UPDLOCK, READPAST) ORDER BY TimeStamp ASC) 
			DELETE FROM message 
			OUTPUT deleted.Id, deleted.CorrelationId, deleted.ReplyToAddress, 
			deleted.Recoverable, deleted.MessageIntent, deleted.TimeToBeReceived, deleted.Headers, deleted.Body;";


        const string SqlPurge = @"DELETE FROM [{0}]";

        static readonly ILog Logger = LogManager.GetLogger("Transports.SqlServer");
    }
}