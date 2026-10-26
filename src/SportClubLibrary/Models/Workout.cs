using SportClubLibrary.Interfaces;

namespace SportClubLibrary.Models
{
    public class Workout : IEntity
    {
        public int Id { get; set; }
        public DateTime Time { get; set; } 
        public int DurationMin { get; set; } 
        public string Type { get; set; }

        public int? CatalogItemId { get; set; }
        public string Hall { get; set; } 

        public Trainer Coach { get; set; }

        public List<Client> Participants { get; set; } = new List<Client>();

        public int MaxParticipants { get; set; } = 10; 

        [System.Text.Json.Serialization.JsonIgnore]
        public string CoachName => Coach?.FullName ?? "Не призначено";

        [System.Text.Json.Serialization.JsonIgnore]
        public string ParticipantsCount => $"{Participants.Count} / {MaxParticipants}";

        [System.Text.Json.Serialization.JsonIgnore]
        public string TimeView => Time.ToString("dd.MM.yyyy HH:mm");
    }
}