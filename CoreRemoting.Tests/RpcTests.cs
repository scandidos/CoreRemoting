using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CoreRemoting.Channels;
using CoreRemoting.Serialization;
using CoreRemoting.Tests.ExternalTypes;
using CoreRemoting.Tests.Tools;
using Xunit;
using Xunit.Abstractions;

namespace CoreRemoting.Tests
{
    [Collection("CoreRemoting")]
    public class RpcTests : IClassFixture<ServerFixture>
    {
        private readonly ServerFixture _serverFixture;
        private readonly ITestOutputHelper _testOutputHelper;
        private bool _remoteServiceCalled;

        protected virtual IServerChannel ServerChannel => null;

        protected virtual IClientChannel ClientChannel => null;

        public RpcTests(ServerFixture serverFixture, ITestOutputHelper testOutputHelper)
        {
            _serverFixture = serverFixture;
            _testOutputHelper = testOutputHelper;

            _serverFixture.TestService.TestMethodFake = arg =>
            {
                _remoteServiceCalled = true;
                return arg;
            };

            _serverFixture.Start(ServerChannel);
        }

        [Fact]
        public void Call_on_Proxy_should_be_invoked_on_remote_service()
        {
            void ClientAction()
            {
                try
                {
                    var stopWatch = new Stopwatch();
                    stopWatch.Start();

                    using var client = new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0,
                        MessageEncryption = false,
                        Channel = ClientChannel,
                        ServerPort = _serverFixture.Server.Config.NetworkPort,
                    });

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Creating client took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();

                    client.Connect();

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Establishing connection took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();

                    var proxy = client.CreateProxy<ITestService>();

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Creating proxy took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();

                    var result = proxy.TestMethod("test");

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Remote method invocation took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();

                    var result2 = proxy.TestMethod("test");

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Second remote method invocation took {stopWatch.ElapsedMilliseconds} ms");

                    Assert.Equal("test", result);
                    Assert.Equal("test", result2);

                    proxy.MethodWithOutParameter(out int methodCallCount);

                    Assert.Equal(1, methodCallCount);
                }
                catch (Exception e)
                {
                    _testOutputHelper.WriteLine(e.ToString());
                    throw;
                }
            }

            var clientThread = new Thread(ClientAction);
            clientThread.Start();
            clientThread.Join();

            Assert.True(_remoteServiceCalled);
            Assert.Equal(0, _serverFixture.ServerErrorCount);
        }

        [Fact]
        public void Call_on_Proxy_should_be_invoked_on_remote_service_with_MessageEncryption()
        {
            _serverFixture.Server.Config.MessageEncryption = true;

            void ClientAction()
            {
                try
                {
                    var stopWatch = new Stopwatch();
                    stopWatch.Start();

                    using var client = new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0,
                        Channel = ClientChannel,
                        ServerPort = _serverFixture.Server.Config.NetworkPort,
                        MessageEncryption = true,
                    });

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Creating client took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();

                    client.Connect();

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Establishing connection took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();

                    var proxy = client.CreateProxy<ITestService>();

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Creating proxy took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();

                    var result = proxy.TestMethod("test");

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Remote method invocation took {stopWatch.ElapsedMilliseconds} ms");
                    stopWatch.Reset();
                    stopWatch.Start();

                    var result2 = proxy.TestMethod("test");

                    stopWatch.Stop();
                    _testOutputHelper.WriteLine($"Second remote method invocation took {stopWatch.ElapsedMilliseconds} ms");

                    Assert.Equal("test", result);
                    Assert.Equal("test", result2);
                }
                catch (Exception e)
                {
                    _testOutputHelper.WriteLine(e.ToString());
                    throw;
                }
            }

            var clientThread = new Thread(ClientAction);
            clientThread.Start();
            clientThread.Join();

            _serverFixture.Server.Config.MessageEncryption = false;

            Assert.True(_remoteServiceCalled);
            Assert.Equal(0, _serverFixture.ServerErrorCount);
        }

        [Fact]
        public void Delegate_invoked_on_server_should_callback_client()
        {
            string argumentFromServer = null;

            void ClientAction()
            {
                try
                {
                    using var client = new RemotingClient(
                        new ClientConfig()
                        {
                            ConnectionTimeout = 0,
                            Channel = ClientChannel,
                            MessageEncryption = false,
                            ServerPort = _serverFixture.Server.Config.NetworkPort,
                        });

                    client.Connect();

                    var proxy = client.CreateProxy<ITestService>();
                    proxy.TestMethodWithDelegateArg(arg => argumentFromServer = arg);
                }
                catch (Exception e)
                {
                    _testOutputHelper.WriteLine(e.ToString());
                    throw;
                }
            }

            var clientThread = new Thread(ClientAction);
            clientThread.Start();
            clientThread.Join();

            Assert.Equal("test", argumentFromServer);
            Assert.Equal(0, _serverFixture.ServerErrorCount);
        }

        [Fact]
        public void Events_should_work_remotely()
        {
            bool serviceEventCalled = false;
            bool customDelegateEventCalled = false;

            using var client = new RemotingClient(
                new ClientConfig()
                {
                    ConnectionTimeout = 0,
                    SendTimeout = 0,
                    Channel = ClientChannel,
                    MessageEncryption = false,
                    ServerPort = _serverFixture.Server.Config.NetworkPort,
                });

            client.Connect();

            var proxy = client.CreateProxy<ITestService>();

            var serviceEventResetEvent = new ManualResetEventSlim(initialState: false);
            var customDelegateEventResetEvent = new ManualResetEventSlim(initialState: false);

            proxy.ServiceEvent += () =>
            {
                serviceEventCalled = true;
                serviceEventResetEvent.Set();
            };

            proxy.CustomDelegateEvent += _ =>
            {
                customDelegateEventCalled = true;
                customDelegateEventResetEvent.Set();
            };

            proxy.FireServiceEvent();
            proxy.FireCustomDelegateEvent();

            serviceEventResetEvent.Wait(1000);
            customDelegateEventResetEvent.Wait(1000);

            Assert.True(serviceEventCalled);
            Assert.True(customDelegateEventCalled);
            Assert.Equal(0, _serverFixture.ServerErrorCount);
        }

        [Fact]
        public void External_types_should_work_as_remote_service_parameters()
        {
            DataClass parameterValue = null;

            _serverFixture.TestService.TestExternalTypeParameterFake = arg =>
            {
                _remoteServiceCalled = true;
                parameterValue = arg;
            };

            void ClientAction()
            {
                try
                {
                    using var client = new RemotingClient(new ClientConfig()
                    {
                        ConnectionTimeout = 0,
                        Channel = ClientChannel,
                        MessageEncryption = false,
                        ServerPort = _serverFixture.Server.Config.NetworkPort,
                    });

                    client.Connect();

                    var proxy = client.CreateProxy<ITestService>();
                    proxy.TestExternalTypeParameter(new DataClass() { Value = 42 });

                    Assert.Equal(42, parameterValue.Value);
                }
                catch (Exception e)
                {
                    _testOutputHelper.WriteLine(e.ToString());
                    throw;
                }
            }

            var clientThread = new Thread(ClientAction);
            clientThread.Start();
            clientThread.Join();

            Assert.True(_remoteServiceCalled);
            Assert.Equal(0, _serverFixture.ServerErrorCount);
        }

        [Fact]
        public void Generic_methods_should_be_called_correctly()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                Channel = ClientChannel,
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort,
            });

            client.Connect();
            var proxy = client.CreateProxy<IGenericEchoService>();

            var result = proxy.Echo("Yay");

            Assert.Equal("Yay", result);
        }

        [Fact]
        public void Inherited_methods_should_be_called_correctly()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                Channel = ClientChannel,
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort,
            });

            client.Connect();
            var proxy = client.CreateProxy<ITestService>();

            var result = proxy.BaseMethod();

            Assert.True(result);
        }

        [Fact]
        public void Enum_arguments_should_be_passed_correctly()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                Channel = ClientChannel,
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort
            });

            client.Connect();
            var proxy = client.CreateProxy<IEnumTestService>();

            var resultFirst = proxy.Echo(TestEnum.First);
            var resultSecond = proxy.Echo(TestEnum.Second);

            Assert.Equal(TestEnum.First, resultFirst);
            Assert.Equal(TestEnum.Second, resultSecond);
        }

        [Fact]
        public void Missing_method_throws_RemoteInvocationException()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                InvocationTimeout = 0,
                SendTimeout = 0,
                Channel = ClientChannel,
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort
            });

            // simulate MissingMethodException
            var mb = new CustomMessageBuilder
            {
                ProcessMethodCallMessage = m =>
                {
                    if (m.MethodName == "TestMethod")
                    {
                        m.MethodName = "Missing Method";
                    }
                }
            };

            client.MethodCallMessageBuilder = mb;
            client.Connect();

            var proxy = client.CreateProxy<ITestService>();
            var ex = Assert.Throws<RemoteInvocationException>(() => proxy.TestMethod(null));

            // a localized message similar to "Method 'Missing method' not found"
            Assert.NotNull(ex);
            Assert.Contains("Missing Method", ex.Message);
        }

        [Fact]
        public void Missing_service_throws_RemoteInvocationException()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                InvocationTimeout = 0,
                SendTimeout = 0,
                Channel = ClientChannel,
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort
            });

            client.Connect();

            var proxy = client.CreateProxy<IDisposable>();
            var ex = Assert.Throws<RemoteInvocationException>(() => proxy.Dispose());

            // a localized message similar to "Service 'System.IDisposable' is not registered"
            Assert.NotNull(ex);
            Assert.Contains("IDisposable", ex.Message);
        }

        [Fact]
        public void Error_method_throws_Exception()
        {
            try
            {
                using var client = new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 5,
                    InvocationTimeout = 5,
                    SendTimeout = 5,
                    Channel = ClientChannel,
                    MessageEncryption = false,
                    ServerPort = _serverFixture.Server.Config.NetworkPort
                });

                client.Connect();

                var proxy = client.CreateProxy<ITestService>();
                var ex = Assert.Throws<RemoteInvocationException>(() =>
                    proxy.Error(nameof(Error_method_throws_Exception)))
                        .GetInnermostException();

                Assert.NotNull(ex);
                Assert.Equal(nameof(Error_method_throws_Exception), ex.Message);
            }
            finally
            {
                // reset the error counter for other tests
                _serverFixture.ServerErrorCount = 0;
            }
        }

        [Fact]
        public async Task ErrorAsync_method_throws_Exception()
        {
            try
            {
                using var client = new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 5,
                    InvocationTimeout = 5,
                    SendTimeout = 5,
                    Channel = ClientChannel,
                    MessageEncryption = false,
                    ServerPort = _serverFixture.Server.Config.NetworkPort
                });

                client.Connect();

                var proxy = client.CreateProxy<ITestService>();
                var ex = (await Assert.ThrowsAsync<RemoteInvocationException>(async () =>
                    await proxy.ErrorAsync(nameof(ErrorAsync_method_throws_Exception))))
                        .GetInnermostException();

                Assert.NotNull(ex); 
                Assert.Equal(nameof(ErrorAsync_method_throws_Exception), ex.Message);
            }
            finally
            {
                // reset the error counter for other tests
                _serverFixture.ServerErrorCount = 0;
            }
        }

        [Fact]
        public void NonSerializableError_method_throws_Exception()
        {
            try
            {
                using var client = new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 5,
                    InvocationTimeout = 5,
                    SendTimeout = 5,
                    Channel = ClientChannel,
                    MessageEncryption = false,
                    ServerPort = _serverFixture.Server.Config.NetworkPort
                });

                client.Connect();

                var proxy = client.CreateProxy<ITestService>();
                var ex = Assert.Throws<RemoteInvocationException>(() =>
                    proxy.NonSerializableError("Hello", "Serializable", "World"))
                        .GetInnermostException();

                Assert.NotNull(ex);
                Assert.IsType<SerializableException>(ex);

                if (ex is SerializableException sx)
                {
                    Assert.Equal("NonSerializable", sx.SourceTypeName);
                    Assert.Equal("Hello", ex.Message);
                    Assert.Equal("Serializable", ex.Data["Serializable"]);
                    Assert.Equal("World", ex.Data["World"]);
                    Assert.NotNull(ex.StackTrace);
                }
            }
            finally
            {
                // reset the error counter for other tests
                _serverFixture.ServerErrorCount = 0;
            }
        }

        [Fact]
        public void Failing_component_constructor_throws_RemoteInvocationException()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 3,
                InvocationTimeout = 3,
                SendTimeout = 3,
                Channel = ClientChannel,
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort,
            });

            client.Connect();

            var proxy = client.CreateProxy<IFailingService>();
            var ex = Assert.Throws<RemoteInvocationException>(() => proxy.Hello());

            Assert.NotNull(ex);
            Assert.Contains("FailingService", ex.Message);
        }

        [Fact]
        public async Task Disposed_client_subscription_doesnt_break_other_clients()
        {
            async Task Roundtrip(bool encryption)
            {
                var oldEncryption = _serverFixture.Server.Config.MessageEncryption;
                _serverFixture.Server.Config.MessageEncryption = encryption;

                try
                {
                    RemotingClient CreateClient() => new RemotingClient(new ClientConfig()
                    {
                        Channel = ClientChannel,
                        ServerPort = _serverFixture.Server.Config.NetworkPort,
                        MessageEncryption = encryption,
                    });

                    using var client1 = CreateClient();
                    using var client2 = CreateClient();

                    client1.Connect();
                    client2.Connect();

                    var proxy1 = client1.CreateProxy<ITestService>();
                    var fired1 = new TaskCompletionSource<bool>();
                    proxy1.ServiceEvent += () => fired1.TrySetResult(true);

                    var proxy2 = client2.CreateProxy<ITestService>();
                    var fired2 = new TaskCompletionSource<bool>();
                    proxy2.ServiceEvent += () => fired2.TrySetResult(true);

                    // early disposal, proxy1 subscription isn't canceled
                    client1.Disconnect();

                    proxy2.FireServiceEvent();
                    Assert.True(await fired2.Task);
                    Assert.True(fired2.Task.IsCompleted);
                    Assert.False(fired1.Task.IsCompleted);
                }
                finally
                {
                    _serverFixture.Server.Config.MessageEncryption = oldEncryption;
                }
            }

            // works!
            await Roundtrip(encryption: false);

            // fails!
            await Roundtrip(encryption: true);
        }

        [Fact]
        public void DataTable_roundtrip_works_issue60()
        {
            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                InvocationTimeout = 0,
                SendTimeout = 0,
                Channel = ClientChannel,
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort,
            });

            client.Connect();
            var proxy = client.CreateProxy<ITestService>();

            var dt = new DataTable();
            dt.TableName = "Issue60";
            dt.Columns.Add("CODE");
            dt.Rows.Add(dt.NewRow());
            dt.AcceptChanges();

            var dt2 = proxy.TestDt(dt, 1);
            Assert.NotNull(dt2);
        }

        [Fact]
        public void Large_messages_are_sent_and_received()
        {
            // max payload size, in bytes
            var maxSize = 2 * 1024 * 1024 + 1;

            using var client = new RemotingClient(new ClientConfig()
            {
                ConnectionTimeout = 0,
                InvocationTimeout = 0,
                SendTimeout = 0,
                Channel = ClientChannel,
                MessageEncryption = false,
                ServerPort = _serverFixture.Server.Config.NetworkPort,
            });

            client.Connect();
            var proxy = client.CreateProxy<ITestService>();

            // shouldn't throw exceptions
            Roundtrip("Payload", maxSize);
            Roundtrip(new byte[] { 1, 2, 3, 4, 5 }, maxSize);
            Roundtrip(new int[] { 12345, 67890 }, maxSize);

            void Roundtrip<T>(T payload, int maxSize) where T : class
            {
                var lastSize = 0;
                try
                {
                    while (true)
                    {
                        // a -> aa -> aaaa ...
                        var dup = proxy.Duplicate(payload);
                        if (dup.size >= maxSize)
                            break;

                        // save the size for error reporting
                        lastSize = dup.size;
                        payload = dup.duplicate;
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to handle " +
                        $"payload larger than {lastSize}: {ex.Message}", ex);
                }
            }
        }

        [Fact]
        public void BeforeCall_and_AfterCall_events_are_triggered_on_success()
        {
            var beforeCallFired = 0;
            void BeforeCall(object sender, ServerRpcContext e) =>
                Interlocked.Increment(ref beforeCallFired);

            var afterCallFired = 0;
            void AfterCall(object sender, ServerRpcContext e) =>
                Interlocked.Increment(ref afterCallFired);

            _serverFixture.Server.BeforeCall += BeforeCall;
            _serverFixture.Server.AfterCall += AfterCall;

            try
            {
                using var client = new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 0,
                    InvocationTimeout = 0,
                    SendTimeout = 0,
                    Channel = ClientChannel,
                    MessageEncryption = false,
                    ServerPort = _serverFixture.Server.Config.NetworkPort,
                });

                client.Connect();

                // test one-way method
                var proxy = client.CreateProxy<ITestService>();
                proxy.OneWayMethod();

                // test normal method
                Assert.Equal("Hello", proxy.Echo("Hello"));

                Assert.Equal(2, beforeCallFired);
                Assert.Equal(2, afterCallFired);
            }
            finally
            {
                _serverFixture.Server.AfterCall -= AfterCall;
                _serverFixture.Server.BeforeCall -= BeforeCall;
            }
        }

        [Fact]
        public void BeforeCall_and_AfterCall_events_are_triggered_on_failures()
        {
            var beforeCallFired = 0;
            void BeforeCall(object sender, ServerRpcContext e) =>
                Interlocked.Increment(ref beforeCallFired);

            var afterCallFired = 0;
            void AfterCall(object sender, ServerRpcContext e) =>
                Interlocked.Increment(ref afterCallFired);

            _serverFixture.Server.BeforeCall += BeforeCall;
            _serverFixture.Server.AfterCall += AfterCall;

            try
            {
                using var client = new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 0,
                    InvocationTimeout = 0,
                    SendTimeout = 0,
                    Channel = ClientChannel,
                    MessageEncryption = false,
                    ServerPort = _serverFixture.Server.Config.NetworkPort,
                });

                client.Connect();

                // test failing method
                var proxy = client.CreateProxy<ITestService>();
                Assert.Throws<RemoteInvocationException>(() => proxy.Error("Bang"));

                Assert.Equal(1, beforeCallFired);
                Assert.Equal(1, afterCallFired);
            }
            finally
            {
                _serverFixture.Server.AfterCall -= AfterCall;
                _serverFixture.Server.BeforeCall -= BeforeCall;
            }
        }

        [Fact]
        public void BeginCall_event_handler_can_intercept_and_cancel_method_calls()
        {
            var counter = 0;

            void InterceptMethodCalls(object sender, ServerRpcContext e)
            {
                Interlocked.Increment(ref counter);

                // swap Echo and Reverse methods
                e.MethodCallMessage.MethodName = e.MethodCallMessage.MethodName switch
                {
                    "Echo" => "Reverse",
                    "Reverse" => "Echo",
                    var others => others
                };

                // disable IHobbitService
                if (e.MethodCallMessage.ServiceName.Contains("IHobbitService"))
                {
                    e.Cancel = true;
                }
            }

            _serverFixture.Server.BeginCall += InterceptMethodCalls;
            try
            {
                using var client = new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 0,
                    InvocationTimeout = 0,
                    SendTimeout = 0,
                    Channel = ClientChannel,
                    MessageEncryption = false,
                    ServerPort = _serverFixture.Server.Config.NetworkPort,
                });

                client.Connect();

                // try swapped methods
                var proxy = client.CreateProxy<ITestService>();
                Assert.Equal("321", proxy.Echo("123"));
                Assert.Equal("Hello", proxy.Reverse("Hello"));

                // try disabled service
                var hobbit = client.CreateProxy<IHobbitService>();
                Assert.Throws<RemoteInvocationException>(() =>
                    hobbit.QueryHobbits(h => h.LastName != ""));

                // check interception counter
                Assert.Equal(3, counter);
            }
            finally
            {
                _serverFixture.Server.BeginCall -= InterceptMethodCalls;
            }
        }

        [Fact]
        public void Authentication_is_taken_into_account_and_RejectCall_event_is_fired()
        {
            var rejectedMethod = string.Empty;
            void RejectCall(object sender, ServerRpcContext e) =>
                rejectedMethod = e.MethodCallMessage.MethodName;

            _serverFixture.Server.Config.AuthenticationRequired = true;
            _serverFixture.Server.RejectCall += RejectCall;
            try
            {
                using var client = new RemotingClient(new ClientConfig()
                {
                    ConnectionTimeout = 0,
                    InvocationTimeout = 0,
                    SendTimeout = 0,
                    Channel = ClientChannel,
                    MessageEncryption = false,
                    ServerPort = _serverFixture.Server.Config.NetworkPort,
                });

                client.Connect();

                var proxy = client.CreateProxy<IFailingService>();
                var ex = Assert.Throws<RemoteInvocationException>(() => proxy.Hello());

                // Session is not authenticated
                Assert.Contains("authenticated", ex.Message);

                // Method call was rejected
                Assert.Equal("Hello", rejectedMethod);
            }
            finally
            {
                _serverFixture.Server.Config.AuthenticationRequired = false;
                _serverFixture.Server.RejectCall -= RejectCall;
            }
        }
    }
}