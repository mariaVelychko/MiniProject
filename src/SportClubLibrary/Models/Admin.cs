namespace SportClubLibrary.Models
{
    public class Admin : Person
    {
        public override string GetRoleInfo() => "Адміністратор";
    }
}