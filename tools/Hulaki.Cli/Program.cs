using System;
using System.Threading;
using Hulaki.Cli;

return await Cli.RunAsync(args, Console.Out, Console.Error, handler: null, CancellationToken.None).ConfigureAwait(false);
