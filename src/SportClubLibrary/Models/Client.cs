namespace SportClubLibrary.Models
{
    public class Client : Person
    {
        public override string GetRoleInfo()
        {
            return "Клієнт клубу";
        }
    }
}