using System.Net;
using System.Net.Http;
using System.Web.Http;
using BusinessModel;
using BusinessServiceInterface;
using RestWebAPI.Filters;

namespace RestWebAPI.Controllers
{
    [RoutePrefix("api/login")]
    public class LoginController : ApiController
    {
        #region Dependencies
        private readonly ILoginService _loginService;
        #endregion

        #region Constructor
        public LoginController(ILoginService loginService)
        {
            _loginService = loginService;
        }
        #endregion

        [Route("UserLogin")]
        [HttpPost]
        public HttpResponseMessage UserLogin([FromBody] LoginBORequest objBORequest)
        {
            UserLoginInfoBOResponse objBOResponse = _loginService.UserLoginInfo(objBORequest);
            if (objBOResponse.userId != 0)
            {
                string access_token = TokenController.GenerateToken(objBOResponse.firstName, 2);
                return Request.CreateResponse(HttpStatusCode.OK, access_token);
            }
            else
            {
                return Request.CreateResponse(HttpStatusCode.NotFound);
            }
        }

        [Route("GetEmployeeList")]
        [JwtAuthentication]
        [HttpGet]
        public IHttpActionResult GetEmployeeList([FromUri] int userId)
        {
            var objBOResponse = _loginService.GetEmployeeList(userId);
            return Ok(objBOResponse);
        }
    }
}
