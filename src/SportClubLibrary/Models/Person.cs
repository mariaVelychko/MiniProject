using SportClubLibrary.Interfaces;
using System.Text.Json.Serialization;

namespace SportClubLibrary.Models 
{ 
    [JsonDerivedType(typeof(Client), typeDiscriminator: "client")]
    [JsonDerivedType(typeof(Trainer), typeDiscriminator: "trainer")]
    [JsonDerivedType(typeof(Admin), typeDiscriminator: "admin")]

    public abstract class Person : IEntity
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }

        [JsonIgnore]
        public string RoleView => GetRoleInfo();
        public abstract string GetRoleInfo();

        public override string ToString() => FullName;
    }
}