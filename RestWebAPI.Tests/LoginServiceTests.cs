using System.Collections.Generic;
using BusinessModel;
using BusinessService;
using DataModel;
using DataServiceInterface;
using Moq;
using NUnit.Framework;

namespace RestWebAPI.Tests
{
    /// <summary>
    /// Unit tests for LoginService.
    ///
    /// These tests mock ILoginDataService so that the business logic in LoginService
    /// is tested in complete isolation — no database driver, no EF context, no Dapper.
    /// This is the concrete payoff of the interface-per-layer design: the seam between
    /// BusinessService and DataService is injectable and therefore mockable.
    /// </summary>
    [TestFixture]
    public class LoginServiceTests
    {
        private Mock<ILoginDataService> _mockDataService;
        private LoginService _sut; // System Under Test

        [SetUp]
        public void SetUp()
        {
            _mockDataService = new Mock<ILoginDataService>();
            _sut = new LoginService(_mockDataService.Object);
        }

        // -----------------------------------------------------------------------
        // UserLoginInfo tests
        // -----------------------------------------------------------------------

        [Test]
        public void UserLoginInfo_WhenUserExists_ReturnsMappedBOResponse()
        {
            // Arrange
            var request = new LoginBORequest { username = "jdoe", password = "secret" };

            _mockDataService
                .Setup(ds => ds.UserLoginInfo(It.IsAny<LoginRequest>()))
                .Returns(new UserLoginInfoResponse
                {
                    SRNO      = 42,
                    Firstname = "Joseph",
                    Lastname  = "Doe",
                    Email     = "jdoe@company.com",
                    Active    = "Y"
                });

            // Act
            UserLoginInfoBOResponse result = _sut.UserLoginInfo(request);

            // Assert
            Assert.That(result.userId,    Is.EqualTo(42));
            Assert.That(result.firstName, Is.EqualTo("Joseph"));
            Assert.That(result.lastName,  Is.EqualTo("Doe"));
            Assert.That(result.email,     Is.EqualTo("jdoe@company.com"));
        }

        [Test]
        public void UserLoginInfo_WhenDataServiceReturnsNull_ReturnsEmptyBOResponse()
        {
            // Arrange — simulate user not found in the database
            var request = new LoginBORequest { username = "unknown", password = "wrong" };

            _mockDataService
                .Setup(ds => ds.UserLoginInfo(It.IsAny<LoginRequest>()))
                .Returns((UserLoginInfoResponse)null);

            // Act
            UserLoginInfoBOResponse result = _sut.UserLoginInfo(request);

            // Assert — userId 0 is the sentinel value LoginController checks before issuing a JWT
            Assert.That(result.userId, Is.EqualTo(0));
        }

        [Test]
        public void UserLoginInfo_MapsCredentialsFromBORequestToDataRequest()
        {
            // Arrange — verify the mapper is called with the correct values
            LoginRequest capturedRequest = null;

            _mockDataService
                .Setup(ds => ds.UserLoginInfo(It.IsAny<LoginRequest>()))
                .Callback<LoginRequest>(r => capturedRequest = r)
                .Returns(new UserLoginInfoResponse { SRNO = 1 });

            var boRequest = new LoginBORequest
            {
                username = "jdoe",
                password = "p@ssw0rd",
                udId     = "device-abc"
            };

            // Act
            _sut.UserLoginInfo(boRequest);

            // Assert — LoginBORequest.Create() maps correctly to LoginRequest
            Assert.That(capturedRequest,          Is.Not.Null);
            Assert.That(capturedRequest.UserName, Is.EqualTo("jdoe"));
            Assert.That(capturedRequest.Password, Is.EqualTo("p@ssw0rd"));
            Assert.That(capturedRequest.StrUDID,  Is.EqualTo("device-abc"));
        }

        // -----------------------------------------------------------------------
        // GetEmployeeList tests
        // -----------------------------------------------------------------------

        [Test]
        public void GetEmployeeList_WhenEmployeesExist_ReturnsMappedBOResponse()
        {
            // Arrange
            _mockDataService
                .Setup(ds => ds.GetEmployeeList(It.IsAny<int>()))
                .Returns(new List<EmployeeMaster>
                {
                    new EmployeeMaster { UserId = 1, FName = "Alice", LName = "Smith", Email = "alice@co.com" },
                    new EmployeeMaster { UserId = 2, FName = "Bob",   LName = "Jones", Email = "bob@co.com"   }
                });

            // Act
            BOResponse<EmployeeListBOResponse> result = _sut.GetEmployeeList(userId: 99);

            // Assert
            Assert.That(result.Code,        Is.EqualTo(0));
            Assert.That(result.Data,        Is.Not.Null);
            Assert.That(result.Data.Count,  Is.EqualTo(2));
            Assert.That(result.Data[0].fName, Is.EqualTo("Alice"));
            Assert.That(result.Data[1].fName, Is.EqualTo("Bob"));
        }

        [Test]
        public void GetEmployeeList_WhenNoEmployeesReturned_ReturnsNullData()
        {
            // Arrange — empty list from data layer
            _mockDataService
                .Setup(ds => ds.GetEmployeeList(It.IsAny<int>()))
                .Returns(new List<EmployeeMaster>());

            // Act
            BOResponse<EmployeeListBOResponse> result = _sut.GetEmployeeList(userId: 1);

            // Assert — EmployeeListBOResponse.Create() returns Code 0, Data null for empty list
            Assert.That(result.Code, Is.EqualTo(0));
            Assert.That(result.Data, Is.Null);
        }

        [Test]
        public void GetEmployeeList_PassesUserIdThroughToDataLayer()
        {
            // Arrange — verify the userId param reaches ILoginDataService unchanged
            int capturedUserId = -1;

            _mockDataService
                .Setup(ds => ds.GetEmployeeList(It.IsAny<int>()))
                .Callback<int>(id => capturedUserId = id)
                .Returns(new List<EmployeeMaster>());

            // Act
            _sut.GetEmployeeList(userId: 4893);

            // Assert
            Assert.That(capturedUserId, Is.EqualTo(4893));
        }
    }
}
