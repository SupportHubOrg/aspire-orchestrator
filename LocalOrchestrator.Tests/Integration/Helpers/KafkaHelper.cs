using Confluent.Kafka;
using Confluent.Kafka.Admin;
using System.Collections.Concurrent;
using System.Text.Json;

namespace LocalOrchestrator.Tests.Integration.Helpers;

/// <summary>
/// Helper class to manage Kafka interactions during integration tests.
/// </summary>
public class KafkaHelper : IDisposable
{
    private readonly IConsumer<Ignore, string> _consumer;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentBag<string> _consumedMessages = [];
    private Task? _consumerTask;
    private readonly string _bootstrapServers;

    /// <summary>
    /// Builds a test Kafka consumer which subscribes to a specific topic that needs to be monitored.
    /// </summary>
    /// <param name="bootstrapServers"></param>
    /// <param name="topic"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public KafkaHelper(string? bootstrapServers, string[] topics)
    {
        //Save the Kafka bootstrap servers for publishing
        _bootstrapServers = bootstrapServers ?? throw new ArgumentNullException(nameof(bootstrapServers));

        //Check if the Kafka bootstrap servers are provided, where the bootstrap server is
        if (string.IsNullOrEmpty(bootstrapServers))
            throw new ArgumentNullException(nameof(bootstrapServers));

        // Ensure topics exist before assigning them to a consumer
        EnsureTopicsExistAsync(topics).GetAwaiter().GetResult();

        //Test configuration for the Kafka consumer
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"test-group-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        //Build the Kafka consumer and subscribe to the specified topic
        _consumer = new ConsumerBuilder<Ignore, string>(config).Build();
        _consumer.Subscribe(topics);
    }

    //Ensures that the Kafka topics exist before assigning them to a consumer
    private async Task EnsureTopicsExistAsync(string[] topics)
    {
        var config = new AdminClientConfig { BootstrapServers = _bootstrapServers };
        using var adminClient = new AdminClientBuilder(config).Build();

        foreach (var topic in topics)
        {
            try
            {
                //Attempt to create the topic
                await adminClient.CreateTopicsAsync([
                    new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }
                ]);
            }
            catch (CreateTopicsException e) when (e.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
            {
                //Topic already exists
            }
            catch (Exception)
            {
                //Catch any other exceptions and ignore them
            }
        }
    }

    /// <summary>
    /// Starts the consumption of messages from the subscribed Kafka topic.
    /// </summary>
    public void StartConsuming()
    {
        //Start the Kafka consumer in a separate task
        _consumerTask = Task.Run(async () =>
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = _consumer.Consume(_cts.Token);

                        //If the message is not null, add it to the consumed messages
                        if (consumeResult?.Message != null)
                        {
                            _consumedMessages.Add(consumeResult.Message.Value);
                        }
                    }
                    catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
                    {
                        //Log and swallow the transient error, letting the broker finish provisioning
                        await Task.Delay(500);
                    }
                    catch (Exception)
                    {
                        //Prevent other unexpected broker exceptions from killing the loop
                        await Task.Delay(500);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, _cts.Token);
    }

    /// <summary>
    /// Waits for a specific kafka event type to be present in the consumed messages for the specified number of seconds.
    /// </summary>
    /// <param name="eventType"></param>
    /// <param name="timeoutSeconds"></param>
    /// <returns></returns>
    public async Task<bool> WaitForMessageAsync(string eventType, int timeoutSeconds = 10)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < timeoutAt)
        {
            //Check if the expected event type is present in the consumed messages
            if (_consumedMessages.Any(m => m.Contains($"\"eventType\":\"{eventType}\"", StringComparison.OrdinalIgnoreCase) || m.Contains($"\"eventType\": \"{eventType}\"", StringComparison.OrdinalIgnoreCase)))
                return true;
            
            await Task.Delay(500);
        }
        return false;
    }

    /// <summary>
    /// Disposes of the Kafka consumer and its associated resources.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        if (_consumerTask != null)
        {
            try { _consumerTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        }
        _consumer.Close();
        _consumer.Dispose();
        _cts.Dispose();
    }

    /// <summary>
    /// Publishes a mock event directly to a Kafka topic.
    /// </summary>
    public async Task PublishMockEventAsync(string topic, string eventType, object payloadPayload)
    {
        //Create a Kafka producer
        var config = new ProducerConfig { BootstrapServers = _bootstrapServers };
        using var producer = new ProducerBuilder<Null, string>(config).Build();

        //Create the event envelope
        var messageEnvelope = new
        {
            metadata = new
            {
                eventId = Guid.NewGuid(),
                eventType = eventType,
                version = 1,
                timestamp = DateTime.UtcNow.ToString("O")
            },
            payload = payloadPayload
        };

        //Serialize the event envelope and publish it to the Kafka topic
        var json = JsonSerializer.Serialize(messageEnvelope);
        await producer.ProduceAsync(topic, new Message<Null, string> { Value = json });
    }
}