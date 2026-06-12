using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Web.Http;
using BusinessModel;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace RestWebAPI.Controllers
{
    public class TokenController : ApiController
    {
        /// <summary>
        /// Use the below code to generate a new symmetric secret key:
        ///     var hmac = new HMACSHA256();
        ///     var key = Convert.ToBase64String(hmac.Key);
        /// Store the result in Web.config appSettings under "JwtSecret".
        /// </summary>
        private static readonly string Secret = ConfigurationManager.AppSettings["JwtSecret"];

        public static string GenerateToken(string username, int expireMinutes = 30)
        {
            var symmetricKey = Convert.FromBase64String(Secret);
            var tokenHandler = new JwtSecurityTokenHandler();

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                        {
                            new Claim(ClaimTypes.Name, username)
                        }),

                Expires = DateTime.UtcNow.AddMinutes(expireMinutes),

                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(symmetricKey), SecurityAlgorithms.HmacSha256Signature)
            };

            SecurityToken securityToken = tokenHandler.CreateToken(tokenDescriptor);
            var token = tokenHandler.WriteToken(securityToken);

            return token;
        }

        public static ClaimsPrincipal GetPrincipal(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadToken(token) as JwtSecurityToken;

                if (jwtToken == null)
                    return null;

                var symmetricKey = Convert.FromBase64String(Secret);

                var validationParameters = new TokenValidationParameters()
                {
                    RequireExpirationTime = true,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    IssuerSigningKey = new SymmetricSecurityKey(symmetricKey),
                    ClockSkew = TimeSpan.Zero//by default it's value will be 5 so if you set expire time 5 minutes then token will expire after 10 minutes
                    //When you set closkskew = timespan.zero then it will not add 5 minutes extra in your expire time
                    //It will work based on your expire time
                };
                //This will validate signature and expire time of token.
                var principal = tokenHandler.ValidateToken(token, validationParameters, out _);

                return principal;
            }

            catch (Exception)
            {
                return null;
            }
        }

        public static bool ValidateToken(string token)
        {
            var simplePrinciple = GetPrincipal(token);
            var identity = simplePrinciple?.Identity as ClaimsIdentity;

            if (identity == null)
                return false;

            var userNameClaim = identity.FindFirst(ClaimTypes.Name);

            if (string.IsNullOrEmpty(userNameClaim?.Value))
                return false;

            // More validate to check whether username exists in system

            return true;
        }
    }
}
