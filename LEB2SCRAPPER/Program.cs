using LEB2SCRAPPER.Contracts.Repository.Core;
using LEB2SCRAPPER.Service;
using LEB2SCRAPPER.Service.Contracts.Core;
using LEB2SCRAPPER.Service.Core;
using LEB2SCRAPPER.Repository.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddControllers().AddApplicationPart(typeof(LEB2SCRAPPER.Presentation.AssemblyReference).Assembly);

builder.Services.AddScoped<ICoreAdapterManager, CoreAdapterManager>();
builder.Services.AddScoped<IServiceManager, ServiceManager>();
builder.Services.AddScoped<IRepositoryManager, RepositoryManager>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
