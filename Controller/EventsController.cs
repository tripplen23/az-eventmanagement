using System.Security.Claims;
using EventManagementApi.DTO;
using EventManagementApi.Database;
using EventManagementApi.Entity;
using EventManagementApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace EventManagementApi.Controllers
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class EventsController : ControllerBase
    {
        #region Properties
        private readonly ApplicationDbContext _context;
        private readonly BlobStorageService _blobStorageService;
        private readonly ServiceBusQueueService _serviceBusQueueService;
        private readonly CosmosDbService _cosmosDbService;
        private readonly ILogger<EventsController> _logger;
        private readonly TelemetryClient _telemetryClient;
        #endregion

        #region Constructors
        public EventsController(ApplicationDbContext context, BlobStorageService blobStorageService, ServiceBusQueueService serviceBusQueueService, CosmosDbService cosmosDbService, ILogger<EventsController> logger, TelemetryClient telemetryClient)
        {
            _context = context;
            _blobStorageService = blobStorageService;
            _serviceBusQueueService = serviceBusQueueService;
            _cosmosDbService = cosmosDbService;
            _logger = logger;
            _telemetryClient = telemetryClient;
        }
        #endregion

        #region GET ALL EVENTS - all authenticated users 
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetEvents()
        {
            try
            {
                var events = await _context.Events.ToListAsync();
                _logger.LogInformation("All events retrieved successfully");
                _telemetryClient.TrackEvent("GetAllEvents", new Dictionary<string, string> {
                    { "Count", events.Count.ToString() },
                    { "Timestamp", DateTime.Now.ToString() }
                });
                return Ok(events);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                _logger.LogError(ex, "An error occurred while retrieving all events");
                return StatusCode(500, "An error occurred while retrieving all events");
            }
        }
        #endregion

        #region GET EVENT BY ID - all authenticated users 
        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetEventById(Guid id)
        {
            try
            {
                var foundEvent = await _context.Events.FindAsync(id);
                if (foundEvent == null)
                {
                    return NotFound($"Event with ID {id} not found");
                }

                _telemetryClient.TrackEvent("GetEventById", new Dictionary<string, string> {
                    { "EventId", foundEvent.Id.ToString() },
                    { "Timestamp", DateTime.Now.ToString() }
                });
                _logger.LogInformation($"Event with ID {id} retrieved successfully");
                return Ok(foundEvent);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                _logger.LogError(ex, $"An error occurred while retrieving event with ID {id}");
                return StatusCode(500, $"An error occurred while retrieving event with ID {id}");
            }
        }
        #endregion

        #region CREATE EVENT - EventProvider 
        [HttpPost]
        [Authorize(Policy = "EventProvider")]
        public async Task<IActionResult> CreateEvent([FromBody] EventCreateDto eventDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    _logger.LogWarning("Invalid model state for event: {EventName}", eventDto.Name);
                    return BadRequest("Model state is invalid: " + ModelState);
                }

                var newEvent = new Event
                {
                    Name = eventDto.Name,
                    Description = eventDto.Description,
                    Location = eventDto.Location,
                    Date = eventDto.Date,
                    OrganizerId = eventDto.OrganizerId,
                    TotalSpots = eventDto.TotalSpots
                };

                _context.Events.Add(newEvent);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Event with ID {newEvent.Id} created successfully");

                // TODO: Add event metadata
                var eventMetadata = new EventMetadata
                {
                    Id = Guid.NewGuid().ToString(),
                    EventId = newEvent.Id.ToString(),
                    Type = eventDto.Type,
                    Category = eventDto.Category
                };

                await _cosmosDbService.AddEventMetadataAsync(eventMetadata);
                _logger.LogInformation($"Event metadata with ID {eventMetadata.Id} created successfully");

                _telemetryClient.TrackEvent("CreateEvent", new Dictionary<string, string> {
                    { "EventId", newEvent.Id.ToString() },
                    { "EventName", newEvent.Name },
                    { "TotalSpots", newEvent.TotalSpots.ToString() },
                    { "Type", eventDto.Type},
                    { "Category", eventDto.Category},
                    { "Timestamp", DateTime.Now.ToString() }
                });
                return CreatedAtAction(nameof(GetEventById), new { id = newEvent.Id }, newEvent);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                _logger.LogError(ex, "An error occurred while creating event: {EventName}", eventDto.Name);
                return StatusCode(500, new { Message = "An error occurred while creating the event.", Details = ex.Message });
            }
        }
        #endregion

        #region UPDATE EVENT - Event Providers 
        [HttpPut("{id}")]
        [Authorize(Policy = "EventProvider")]
        public async Task<IActionResult> UpdateEvent(string id, [FromBody] EventUpdateDto eventDto)
        {
            if (!Guid.TryParse(id, out var eventId))
            {
                return BadRequest("Invalid event ID");
            }

            var existingEvent = await _context.Events.FindAsync(eventId);
            if (existingEvent == null)
            {
                return NotFound($"Event with ID {eventId} not found");
            }

            // TODO: Update
            existingEvent.Name = eventDto.Name ?? existingEvent.Name;
            existingEvent.Description = eventDto.Description ?? existingEvent.Description;
            existingEvent.Location = eventDto.Location ?? existingEvent.Location;
            existingEvent.Date = eventDto.Date ?? existingEvent.Date;
            existingEvent.OrganizerId = eventDto.OrganizerId ?? existingEvent.OrganizerId;
            existingEvent.TotalSpots = eventDto.TotalSpots ?? existingEvent.TotalSpots;

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Event with ID {existingEvent.Id} updated successfully");

                _telemetryClient.TrackEvent("UpdateEvent", new Dictionary<string, string> {
                    { "EventId", existingEvent.Id.ToString() },
                    { "EventName", existingEvent.Name },
                    { "TotalSpots", existingEvent.TotalSpots.ToString() },
                    { "Timestamp", DateTime.Now.ToString() }
                });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (!_context.Events.Any(e => e.Id == eventId))
                {
                    return NotFound();
                }
                _telemetryClient.TrackException(ex);
                throw;
            }
            return Ok($"Event {existingEvent.Name} is updated successfully!");
        }
        #endregion

        #region DELETE EVENT - Admins
        [HttpDelete("{id}")]
        [Authorize(Policy = "Admin")]
        public async Task<IActionResult> DeleteEvent(Guid id)
        {
            var eventToDelete = await _context.Events.FindAsync(id);
            if (eventToDelete == null)
            {
                return NotFound($"Event with ID {id} not found");
            }

            _logger.LogInformation($"Deleting event with ID: {id}");

            try
            {
                // Delete event metadata
                var foundEventMetadata = await _cosmosDbService.SearchEventsByEventIdAsync(id.ToString());
                if (foundEventMetadata == null || !foundEventMetadata.Any())
                {
                    _logger.LogWarning($"No metadata found for event ID: {id}");
                }
                else
                {
                    foreach (var metadata in foundEventMetadata)
                    {
                        _logger.LogInformation($"Deleting metadata ID: {metadata.Id}");

                        await _cosmosDbService.DeleteEventMetadataAsync(metadata.Id);
                    }
                }

                // Delete user interactions
                await _cosmosDbService.DeleteUserInteractionsByEventIdAsync(id.ToString());

                // Delete the event from the main database
                _context.Events.Remove(eventToDelete);
                await _context.SaveChangesAsync();

                return Ok($"Event {eventToDelete.Name} and all associated data have been deleted successfully!");
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                _logger.LogError(ex, $"An error occurred while deleting event with ID: {id}");
                return StatusCode(500, "Internal server error");
            }
        }
        #endregion

        #region User can register for an event (BusQueueService)
        [HttpPost("{id}/register")]
        [Authorize]
        public async Task<IActionResult> RegisterForEvent(string id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var foundEvent = await _context.Events.AnyAsync(e => e.Id.ToString() == id);
            if (!foundEvent)
            {
                return NotFound($"There is no event with ID {id}");
            }

            var registration = new EventRegistrationDto
            {
                EventId = id.ToString(),
                UserId = userId,
                Action = "Register"
            };

            var userInteraction = new UserInteraction
            {
                Id = Guid.NewGuid().ToString(),
                EventId = id.ToString(),
                InteractionType = "register",
                UserId = userId
            };

            _telemetryClient.TrackEvent("RegisterForEvent", new Dictionary<string, string> {
                { "EventId", id },
                { "UserId", userId },
                { "Action", "Register" },
                { "Timestamp", DateTime.Now.ToString() }
            });
            await _cosmosDbService.AddUserInteractionAsync(userInteraction);

            await _context.SaveChangesAsync();

            var message = new ServiceBusMessage(new BinaryData(JsonConvert.SerializeObject(registration)))
            {
                SessionId = id.ToString(),
            };

            await _serviceBusQueueService.SendMessageAsync(message);
            return Accepted(new { Message = "Registration request accepted" });
        }
        #endregion

        #region User can unregister from an event (BusQueueService)
        [HttpDelete("{id}/unregister")]
        [Authorize]
        public async Task<IActionResult> UnregisterFromEvent(string id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // TODO: Check if the registration exist
            var registrationExists = await _context.EventRegistrations.AnyAsync(r => r.EventId == id && r.UserId == userId);

            if (!registrationExists)
            {
                return NotFound("Registration not found");
            }

            var registration = new EventRegistrationDto()
            {
                EventId = id.ToString(),
                UserId = userId,
                Action = "Unregister"
            };

            var userInteraction = new UserInteraction
            {
                Id = Guid.NewGuid().ToString(),
                EventId = id.ToString(),
                InteractionType = "unregister",
                UserId = userId
            };
            await _cosmosDbService.AddUserInteractionAsync(userInteraction);

            var message = new ServiceBusMessage(new BinaryData(System.Text.Json.JsonSerializer.Serialize(registration)))
            {
                SessionId = id.ToString()
            };

            await _serviceBusQueueService.SendMessageAsync(message);
            _telemetryClient.TrackEvent("UnregisterFromEvent", new Dictionary<string, string> {
                { "EventId", id },
                { "UserId", userId },
                { "Action", "Unregister" },
                { "Timestamp", DateTime.Now.ToString() }
            });
            return Accepted(new { message = "Unregistration request accepted" });
        }
        #endregion

        #region Upload event images (BlobStorage)
        [HttpPost("{id}/upload-images")]
        [Authorize(Policy = "EventProvider")]
        public async Task<IActionResult> UploadImages(Guid id, List<IFormFile> imageFiles)
        {
            var operation = _telemetryClient.StartOperation<RequestTelemetry>("UploadImages");
            try
            {
                if (imageFiles == null || imageFiles.Count == 0)
                {
                    return BadRequest("No image files provided");
                }

                var eventEntity = await _context.Events.Include(e => e.Images).FirstOrDefaultAsync(e => e.Id == id);
                if (eventEntity == null)
                {
                    return NotFound($"Event with ID {id} not found");
                }

                foreach (var file in imageFiles)
                {
                    if (file.Length > 0)
                    {
                        var imageUrl = await _blobStorageService.UploadFileAsync(file, "eventimages");
                        eventEntity.Images.Add(new EventImage { Url = imageUrl });
                    }
                    else
                    {
                        _logger.LogWarning("File {FileName} is empty", file.FileName);
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(eventEntity.Images.Select(img => img.Url));
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                _logger.LogError(ex, "An error occurred while uploading images for event with ID: {Id}", id);
                throw;
            }
            finally
            {
                _telemetryClient.StopOperation(operation);
            }
        }
        #endregion

        #region Upload event documents (BlobStorage)
        [HttpPost("{id}/upload-documents")]
        [Authorize(Policy = "EventProvider")]
        public async Task<IActionResult> UploadDocuments(Guid id, List<IFormFile> documentFiles)
        {
            if (documentFiles == null || documentFiles.Count == 0)
            {
                return BadRequest("No document files provided");
            }

            var eventEntity = await _context.Events.Include(e => e.Documents).FirstOrDefaultAsync(e => e.Id == id);
            if (eventEntity == null)
            {
                return NotFound($"Event with ID {id} not found");
            }

            foreach (var file in documentFiles)
            {
                if (!IsSupportedDocument(file.FileName))
                {
                    return BadRequest("Unsupported file type.");
                }

                var documentUrl = await _blobStorageService.UploadFileAsync(file, "eventdocuments");
                eventEntity.Documents.Add(new EventDocument { Url = documentUrl });
            }

            _telemetryClient.TrackEvent("UploadEventDocuments", new Dictionary<string, string> {
                { "Count", documentFiles.Count.ToString() },
                { "EventId", id.ToString() }
            });
            await _context.SaveChangesAsync();
            return Ok(eventEntity.Documents.Select(doc => doc.Url));
        }

        private bool IsSupportedDocument(string fileName)
        {
            var supportedTypes = new[] { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "csv" };
            var fileExtension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            return supportedTypes.Contains(fileExtension);
        }
        #endregion

        #region Search event by metadata (CosmosDB NoSQL) - Unfinished - Redis cache integration needed
        [HttpGet("search")]
        [Authorize]
        public async Task<IActionResult> SearchEventsByMetadata([FromQuery] string type, [FromQuery] string category)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(category))
                {
                    return BadRequest(new { Message = "Type and category are required." });
                }
                _logger.LogInformation($"Searching for events with type: {type} and category: {category}");
                var results = await _cosmosDbService.SearchEventsByTypeAndCategoryAsync(type, category);
                _logger.LogInformation($"Search returned {results.Count()} results");
                return Ok(results);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "An error occurred while searching for events.", Details = ex.Message });
            }
        }
        #endregion

        #region Get most registered events (Cosmos DB NoSQL) - Unfinished - Redis cache integration needed
        [HttpGet("most-registered")]
        public async Task<IActionResult> GetMostRegisteredEvents()
        {
            try
            {
                var events = await _cosmosDbService.GetMostRegisteredEventsAsync();
                return Ok(events);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "An error occurred while fetching the most registered events.", Details = ex.Message });
            }
        }
        #endregion
    }
}
