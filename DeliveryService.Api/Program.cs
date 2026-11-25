using DeliveryService.Api;

var app = DeliveryHost.Build(args);
await DeliveryHost.Initialize(app);
await app.RunAsync();

public partial class Program;
