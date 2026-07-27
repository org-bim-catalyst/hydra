using MediatR;

namespace AskLucy.Application.Authentication.Commands.Register;

public sealed record RegisterCommand(string Email, string Password, string? FirstName, string? LastName) : IRequest<AuthResult>;
