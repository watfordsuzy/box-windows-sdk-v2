using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Box.V2.Config;
using Box.V2.JWTAuth;
using Box.V2.Models;
using Box.V2.Test.IntegrationNew.Configuration.Commands;
using Box.V2.Test.IntegrationNew.Configuration.Commands.CleanupCommands;
using Box.V2.Test.IntegrationNew.Configuration.Commands.DisposableCommands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Box.V2.Test.Integration
{
    [TestClass]
    public abstract class IntegrationTestBase
    {
        protected static IBoxClient UserClient;
        protected static IBoxClient AdminClient;
        protected static string UserId;
        protected static bool ClassInit = false;
        protected static bool UserCreated = false;

        protected static Stack<IDisposableCommand> ClassCommands;
        protected static Stack<IDisposableCommand> TestCommands;

        [AssemblyInitialize]
        public static async Task AssemblyInitialize(TestContext testContext)
        {
            var jsonConfig = Environment.GetEnvironmentVariable("INTEGRATION_TESTING_CONFIG");

            if (string.IsNullOrEmpty(jsonConfig))
            {
                Debug.WriteLine("No json config found in environment variables Reading from config.json.");
                jsonConfig = ReadFromJson("config.json");
            }

            var config = BoxConfigBuilder.CreateFromJsonString(jsonConfig)
                .Build();
            var session = new BoxJWTAuth(config);

            var json = JObject.Parse(jsonConfig);
            var adminToken = await session.AdminTokenAsync();
            AdminClient = session.AdminClient(adminToken);

            if (json["userID"] != null && json["userID"].ToString().Length != 0)
            {
                UserId = json["userID"].ToString();
            }
            else
            {
                var user = await CreateNewUser(AdminClient);

                UserId = user.Id;
            }
            var userToken = await session.UserTokenAsync(UserId);
            UserClient = session.UserClient(userToken, UserId);
        }

        [AssemblyCleanup]
        public static async Task AssemblyCleanup()
        {
            if (UserCreated)
            {
                try
                {
                    await AdminClient.UsersManager.DeleteEnterpriseUserAsync(UserId, false, true);

                }
                catch (Exception exp)
                {
                    // Delete will fail if there are content in the user
                    Console.WriteLine(exp.StackTrace);
                }
            }
        }

        [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
        public static void ClassInitialize(TestContext context)
        {
            if (!ClassInit)
            {
                ClassCommands = new Stack<IDisposableCommand>();
            }
        }

        [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
        public static async Task ClassCleanup()
        {
            await DisposeCommands(ClassCommands);
        }

        [TestInitialize]
        public void TestInitialize()
        {
            TestCommands = new Stack<IDisposableCommand>();
        }

        [TestCleanup]
        public async Task TestCleanup()
        {
            await DisposeCommands(TestCommands);
        }

        private static async Task DisposeCommands(Stack<IDisposableCommand> commands)
        {
            while (commands.Count > 0)
            {
                var command = commands.Pop();
                IBoxClient client = GetClient(command);
                await command.Dispose(client);
            }
        }

        protected static Task<BoxUser> CreateNewUser(IBoxClient client)
        {
            var userRequest = new BoxUserRequest
            {
                Name = "IT App User - " + Guid.NewGuid().ToString(), 
                IsPlatformAccessOnly = true
            };
            var user = client.UsersManager.CreateEnterpriseUserAsync(userRequest);
            UserCreated = true;
            return user;
        }

        protected static string GetUniqueName(TestContext testContext)
        {
            return string.Format("{0} - {1}", testContext.TestName, Guid.NewGuid().ToString());
        }

        protected static string GetUniqueName(string resourceName)
        {
            return string.Format("{0} - {1}", resourceName, Guid.NewGuid().ToString());
        }

        public static async Task ExecuteCommand(ICleanupCommand command)
        {
            IBoxClient client = GetClient(command);
            await command.Execute(client);
        }

        public static async Task<string> ExecuteCommand(IDisposableCommand command)
        {
            IBoxClient client = GetClient(command);

            var resourceId = await command.Execute(client);
            if(command.Scope == CommandScope.Test)
            {
                TestCommands.Push(command);
            }
            else
            {
                ClassCommands.Push(command);
            }
            return resourceId;
        }

        private static IBoxClient GetClient(ICommand command)
        {
            switch (command.AccessLevel)
            {
                case CommandAccessLevel.Admin:
                    return AdminClient;
                case CommandAccessLevel.User:
                    return UserClient;
                default:
                    return UserClient;
            }
        }

        public static string GetSmallFilePath()
        {
            return string.Format(AppDomain.CurrentDomain.BaseDirectory + "/TestData/smalltest.pdf");
        }

        public static string GetSmallFileV2Path()
        {
            return string.Format(AppDomain.CurrentDomain.BaseDirectory + "/TestData/smalltestV2.pdf");
        }

        public static string ReadFromJson(string path)
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            return File.ReadAllText(filePath);
        }

        public static async Task<BoxFile> CreateSmallFile(string parentId = "0", CommandScope commandScope = CommandScope.Test, CommandAccessLevel accessLevel = CommandAccessLevel.User)
        {
            var createFileCommand = new CreateFileCommand(GetUniqueName("file"), GetSmallFilePath(), parentId, commandScope, accessLevel);
            await ExecuteCommand(createFileCommand);
            return createFileCommand.File;
        }

        public static async Task<BoxFile> CreateSmallFileAsAdmin(string parentId)
        {
            return await CreateSmallFile(parentId, CommandScope.Test, CommandAccessLevel.Admin);
        }

        public static async Task DeleteFile(string fileId)
        {
            await ExecuteCommand(new DeleteFileCommand(fileId));
        }

        public static async Task<BoxFolder> CreateFolder(string parentId = "0", CommandScope commandScope = CommandScope.Test, CommandAccessLevel accessLevel = CommandAccessLevel.User)
        {
            var createFolderCommand = new CreateFolderCommand(GetUniqueName("folder"), parentId, commandScope, accessLevel);
            await ExecuteCommand(createFolderCommand);
            return createFolderCommand.Folder;
        }

        public static async Task<BoxFolder> CreateFolderAsAdmin(string parentId)
        {
            return await CreateFolder(parentId, CommandScope.Test, CommandAccessLevel.Admin);
        }

        public MemoryStream CreateBigFileInMemoryStream(long fileSize)
        {
            var dataArray = new byte[fileSize];
            new Random().NextBytes(dataArray);
            var memoryStream = new MemoryStream(dataArray);
            return memoryStream;
        }

        public static async Task<BoxRetentionPolicy> CreateRetentionPolicy(string folderId = "0", CommandScope commandScope = CommandScope.Test)
        {
            var createRetentionPolicyCommand = new CreateRetentionPolicyCommand(folderId, GetUniqueName("policy"), commandScope);
            await ExecuteCommand(createRetentionPolicyCommand);
            return createRetentionPolicyCommand.Policy;
        }
    }
}