using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

var securityKey = "REPLACE_WITH_A_SECURE_RANDOM_STRING_AT_LEAST_32_CHARS";
var validIssuer = "BoardVerseAPI";
var validAudience = "BoardVerseApp";
var userId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd01");
var username = "demoplayer1";
var email = "player1@boardverse.dev";
var role = "Player";

// Generate JWT
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(securityKey));
var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

var claims = new List<Claim>
{
    new(ClaimTypes.NameIdentifier, userId.ToString()),
    new(ClaimTypes.Name, username),
    new(ClaimTypes.Email, email),
    new(ClaimTypes.Role, role),
    new("provider", "Local")
};

var token = new JwtSecurityToken(
    issuer: validIssuer,
    audience: validAudience,
    claims: claims,
    expires: DateTime.UtcNow.AddHours(1),
    signingCredentials: credentials);

var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
Console.WriteLine(tokenString);
