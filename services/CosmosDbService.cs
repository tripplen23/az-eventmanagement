using EventManagementApi.Entity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace EventManagementApi.Services
{
    public class CosmosDbService
    {
        private readonly Container _eventMetadataContainer;
        private readonly Container _userInteractionsContainer;
        private readonly Container _eventsContainer;
        private readonly ILogger<CosmosDbService> _logger;

        public CosmosDbService(CosmosClient cosmosClient, IConfiguration configuration, ILogger<CosmosDbService> logger)
        {
            var databaseName = configuration["CosmosDb:DatabaseName"];
            var EventMetadataContainerName = configuration["CosmosDb:EventMetadataContainer"];
            var userInteractionsContainerName = configuration["CosmosDb:UserInteractionsContainer"];

            _eventMetadataContainer = cosmosClient.GetContainer(databaseName, EventMetadataContainerName);
            _userInteractionsContainer = cosmosClient.GetContainer(databaseName, userInteractionsContainerName);
            _eventsContainer = cosmosClient.GetContainer(databaseName, "EventsContainer");

            _logger = logger;
        }

        #region Add Event Metadata
        public async Task AddEventMetadataAsync(EventMetadata metadata)
        {
            await _eventMetadataContainer.CreateItemAsync(metadata, new PartitionKey(metadata.Id));
        }
        #endregion

        #region Delete Event Metadata
        public async Task DeleteEventMetadataAsync(string metadataId)
        {
            await _eventMetadataContainer.DeleteItemAsync<EventMetadata>(metadataId, new PartitionKey(metadataId));
        }
        #endregion

        #region Add User Interaction
        public async Task AddUserInteractionAsync(UserInteraction interaction)
        {
            await _userInteractionsContainer.CreateItemAsync(interaction, new PartitionKey(interaction.Id));
        }
        #endregion

        #region Delete User Interaction
        public async Task DeleteUserInteractionAsync(string interactionId)
        {
            await _userInteractionsContainer.DeleteItemAsync<UserInteraction>(interactionId, new PartitionKey(interactionId));
        }
        #endregion

        #region Events Metadata methods 
        public async Task<IEnumerable<EventMetadata>> SearchEventsByCriteriaAsync(string queryText, params (string Name, object Value)[] parameters)
        {
            var query = new QueryDefinition(queryText);
            foreach (var parameter in parameters)
            {
                query = query.WithParameter(parameter.Name, parameter.Value);
            }

            var iterator = _eventMetadataContainer.GetItemQueryIterator<EventMetadata>(query);
            var results = new List<EventMetadata>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response.ToList());
            }

            return results;
        }

        public async Task<IEnumerable<EventMetadata>> SearchEventsByEventIdAsync(string eventId)
        {
            var queryText = "SELECT * FROM c WHERE c.EventId = @eventId";
            _logger.LogInformation($"Executing query: {queryText} with parameter: @eventId={eventId}");
            try
            {
                var results = await SearchEventsByCriteriaAsync(queryText, ("@eventId", eventId));
                _logger.LogInformation($"Query returned {results.Count()} results");
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An error occurred while searching for event metadata with event ID: {eventId}");
                throw;
            }
        }

        // !Unfinished
        public async Task<IEnumerable<EventMetadata>> SearchEventsByTypeAndCategoryAsync(string type, string category)
        {
            //var queryText = "SELECT * FROM c WHERE CONTAINS(LOWER(c.Type), LOWER(@type)) AND CONTAINS(LOWER(c.Category), LOWER(@category))";
            var queryText = "SELECT * FROM c WHERE c.type = @type";
            //var queryText = "SELECT * FROM c";
            _logger.LogInformation($"Executing query: {queryText} with parameters: @type={type}, @category={category}");

            try
            {
                var results = await SearchEventsByCriteriaAsync(queryText, ("@type", type), ("@category", category));
                _logger.LogInformation($"Query returned {results.Count()} results");
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An error occurred while searching for events with type: {type} and category: {category}");
                throw;
            }
        }
        #endregion

        #region User Interaction methods
        // !Unfinished
        public async Task<IEnumerable<Event>> GetMostRegisteredEventsAsync()
        {
            var query = new QueryDefinition("SELECT c.eventId, COUNT(c.id) as registrations FROM c WHERE c.interactionType = 'register' GROUP BY c.eventId ORDER BY registrations DESC");
            var iterator = _userInteractionsContainer.GetItemQueryIterator<UserInteraction>(query);
            var results = new List<UserInteraction>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response.ToList());
            }

            var eventIds = results.Select(r => r.EventId).Distinct();
            var events = new List<Event>();

            foreach (var eventId in eventIds)
            {
                var eventResponse = await _eventsContainer.ReadItemAsync<Event>(eventId, new PartitionKey(eventId));
                events.Add(eventResponse.Resource);
            }

            return events;
        }

        // Delete all user interactions for an event
        public async Task DeleteUserInteractionsByEventIdAsync(string eventId)
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.eventId = @eventId")
                .WithParameter("@eventId", eventId);

            var iterator = _userInteractionsContainer.GetItemQueryIterator<UserInteraction>(query);
            var results = new List<UserInteraction>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response.ToList());
            }

            foreach (var interaction in results)
            {
                await _userInteractionsContainer.DeleteItemAsync<UserInteraction>(interaction.Id, new PartitionKey(interaction.Id));
                _logger.LogInformation($"Deleted user interaction: {interaction.Id} for event: {eventId}");
            }
        }
        #endregion
    }
}