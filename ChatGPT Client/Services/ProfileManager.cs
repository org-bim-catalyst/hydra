using AskLucy.Models;
using Microsoft.Extensions.Hosting.Internal;
using Newtonsoft.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AskLucy.Services
{
    public class ProfileManager : IProfileManager
    {
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly UserProfileFoldersTemplate? _userProfileFoldersTemplate;

        public ProfileManager(IWebHostEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;

            string templatePath = Path.Combine(_hostingEnvironment.WebRootPath, "user-profile-folders-template.json");

            if (File.Exists(templatePath))
            {
                string template = System.IO.File.ReadAllText(templatePath);
                _userProfileFoldersTemplate = JsonConvert.DeserializeObject<UserProfileFoldersTemplate>(template)!;
            }
        }

        public void CreateProfileDirectoryFromTemplate(string UserId)
        {
            foreach (string folderName in _userProfileFoldersTemplate!.Collection.Select(c => c.Title))
            {
                CreateProfileDirectory(UserId, folderName);
            }
        }

        public void CreateProfileDirectory(string UserId, string FolderName)
        {
            string DirectoryPath = Path.Combine(_hostingEnvironment.WebRootPath, "users", $"{UserId}", "profiles", FolderName);

            DirectoryInfo ProfileDirectory = Directory.CreateDirectory(DirectoryPath);
        }
    }
}