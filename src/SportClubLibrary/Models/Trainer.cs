namespace SportClubLibrary.Models
{
    public class Trainer : Person
    {
        public string Specialization { get; set; }

        public override string GetRoleInfo()
        {
            return $"Тренер: {Specialization}";
        }
    }
}