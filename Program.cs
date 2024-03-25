// MIT License
//
// Copyright (c) 2024 Marcel Joachim Kloubert (https://marcel.coffee)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using DotEnv.Core;

namespace MarcelJKloubert.CliDemo1;

class Program
{
    static readonly char[] _INVALID_FILENAME_CHARS = Path.GetInvalidFileNameChars();

    async static Task<int> Main(string[] args)
    {
        // load environment variables
        // from any .env* file
        // which exists in current working directory
        new EnvLoader().LoadEnv();

        if (args.Length < 1)
        {
            Console.WriteLine("Please define the output directory!");
            return 2;
        }

        var connectionString = Environment.GetEnvironmentVariable("TGF_AZURE_BLOB_STORAGE_CONNECTION_STRING")?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("Please define the connection string to Azure storage account!");
            return 3;
        }

        var containerName = Environment.GetEnvironmentVariable("TGF_AZURE_BLOB_STORAGE_CONTAINER")?.Trim();
        if (string.IsNullOrWhiteSpace(containerName))
        {
            Console.WriteLine("Please define the container name inside Azure storage account!");
            return 4;
        }

        var outputRootPath = Path.GetFullPath(args[0]!);
        if (File.Exists(outputRootPath))
        {
            Console.WriteLine("Output path must not be a file!");
            return 5;
        }

        try
        {
            // ensure root directory for
            // the downloaded blobs exists
            var outputRootDir = new DirectoryInfo(outputRootPath);
            if (!outputRootDir.Exists)
            {
                outputRootDir = Directory.CreateDirectory(outputRootPath);
            }

            var container = new BlobContainerClient(connectionString, containerName);

            var blobEnumerator = container.GetBlobsAsync().GetAsyncEnumerator();
            while (await blobEnumerator.MoveNextAsync())
            {
                try
                {
                    var blob = blobEnumerator.Current;
                    var blobClient = container.GetBlobClient(blob!.Name);

                    var relativeOutputPath = string.Join(
                        "/",
                        blob!.Name
                            .Split("/")
                            .Select((part) =>
                            {
                                // cleanup blob name

                                part = part.Trim();
                                var sanitizedPart = new StringBuilder();

                                for (var i = 0; i < part.Length; i++)
                                {
                                    var charToAdd = part[i];

                                    if (_INVALID_FILENAME_CHARS.Contains(charToAdd))
                                    {
                                        sanitizedPart.Append('_');
                                    }
                                    else
                                    {
                                        sanitizedPart.Append(charToAdd);
                                    }
                                }

                                return sanitizedPart.ToString().Trim();
                            })
                            .Where((part) => part != "")
                    );

                    if (relativeOutputPath == "")
                    {
                        // skip, because the path is invalid
                        continue;
                    }

                    var fullOutputPath = Path.Join(outputRootDir.FullName, relativeOutputPath);

                    var outFile = new FileInfo(fullOutputPath);
                    if (outFile.Exists)
                    {
                        // we do not overwrite existing files
                        throw new IOException(string.Format("'{0}' already exists!", relativeOutputPath));
                    }

                    var outDir = outFile.Directory;
                    if (!outDir!.Exists)
                    {
                        // ensure output directory exists
                        outDir = Directory.CreateDirectory(outDir.FullName);
                    }

                    // start async download
                    Console.Write("Downloading '{0}' ... ", blob.Name);
                    await blobClient.DownloadToAsync(outFile.FullName);
                    Console.WriteLine("✅");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("❌ [{0}] '{1}'", ex.GetType().FullName, ex.Message);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            // global error

            Console.WriteLine("🔥 EXCEPTION: {0}", ex);
            return 1;
        }
    }
}
