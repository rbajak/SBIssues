using SBClientDll;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SBClientDllTest
{
    public class ClientTest
    {
        private readonly SBClientDll.SBClientDll _sut;
        private static SemaphoreSlim semaphore;

        public ClientTest()
        {
            _sut = new SBClientDll.SBClientDll();
        }

        [Fact]
        public void CallLow()
        {
            var res = _sut.CallLow("low");
            Assert.Equal(0, res);
        }

        [Fact]
        public void CallHigh()
        {
            var res = _sut.CallHigh("high");
            Assert.Equal(0, res);
        }

        [Fact]
        public void Session()
        {
            semaphore = new SemaphoreSlim(0, 4);
            List<Task> tasks = new List<Task>();

            for (int i = 0; i < 15; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    var _sut = new SBClientDll.SBClientDll();
                    semaphore.Wait();
                    try
                    {
                        var sesId = Guid.NewGuid().ToString();
                        var res = _sut.CallSession("start", sesId);
                        Assert.Equal(0, res);
                        res = _sut.CallSession("s1", sesId);
                        Assert.Equal(0, res);
                        res = _sut.CallSession("s2", sesId);
                        Assert.Equal(0, res);
                        res = _sut.CallSession("s3", sesId);
                        Assert.Equal(0, res);
                        res = _sut.CallSession("s4", sesId);
                        Assert.Equal(0, res);
                        res = _sut.CallSession("stop", sesId);
                        Assert.Equal(0, res);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }
            semaphore.Release(4);

            var waitTime = new TimeSpan(0, 6, 0);
            bool res = Task.WaitAll(tasks.ToArray(), waitTime);
            Assert.True(res, $"Failed to finish withing {waitTime.TotalMinutes} minutes");
        }

        [Fact]
        public void SessionNotClosed()
        {
            semaphore = new SemaphoreSlim(0, 4);
            List<Task> tasks = new List<Task>();

            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    var _sut = new SBClientDll.SBClientDll();
                    semaphore.Wait();
                    try
                    {

                        var sesId = Guid.NewGuid().ToString();
                        var res = _sut.CallSession("start", sesId);
                        Assert.Equal(0, res);
                        res = _sut.CallSession("s1", sesId);
                        Assert.Equal(0, res);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            semaphore.Release(4);

            var waitTime = new TimeSpan(0, 6, 0);
            bool res = Task.WaitAll(tasks.ToArray(), waitTime);
            Assert.True(res, $"Failed to finish withing {waitTime.TotalMinutes} minutes");
        }

        [Fact]
        public void AuReproduction()
        {
            semaphore = new SemaphoreSlim(0, 4);
            List<Task> tasks = new List<Task>();

            // Full sessions
            for (int i = 0; i < (4 * 6) - 1; i++)
            {
                switch (i % 4)
                {
                    case 0: //Session full
                        {
                            tasks.Add(Task.Run(() =>
                            {
                                var _sut = new SBClientDll.SBClientDll();
                                semaphore.Wait();
                                try
                                {
                                    var sesId = Guid.NewGuid().ToString();
                                    var res = _sut.CallSession("start", sesId);
                                    Assert.Equal(0, res);
                                    res = _sut.CallSession("s1", sesId);
                                    Assert.Equal(0, res);
                                    res = _sut.CallSession("s2", sesId);
                                    Assert.Equal(0, res);
                                    res = _sut.CallSession("s3", sesId);
                                    Assert.Equal(0, res);
                                    res = _sut.CallSession("s4", sesId);
                                    Assert.Equal(0, res);
                                    res = _sut.CallSession("stop", sesId);
                                    Assert.Equal(0, res);
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }
                        ));
                            break;
                        }
                    case 1: //Session broken - not stopped
                        {
                            tasks.Add(Task.Run(() =>
                            {
                                var _sut = new SBClientDll.SBClientDll();

                                semaphore.Wait();
                                try
                                {
                                    var sesId = Guid.NewGuid().ToString();
                                    var res = _sut.CallSession("start", sesId);
                                    Assert.Equal(0, res);
                                    res = _sut.CallSession("s1", sesId);
                                    Assert.Equal(0, res);
                                    res = _sut.CallSession("s2", sesId);
                                    Assert.Equal(0, res);
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }));
                            break;
                        }
                    case 2: //High prio
                        {
                            tasks.Add(Task.Run(() =>
                            {
                                var _sut = new SBClientDll.SBClientDll();

                                semaphore.Wait();
                                try
                                {
                                    var res = _sut.CallHigh("high");
                                    Assert.Equal(0, res);
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }));
                            break;
                        }
                    case 3: //Low prio
                        {
                            tasks.Add(Task.Run(() =>
                            {
                                var _sut = new SBClientDll.SBClientDll();

                                semaphore.Wait();
                                try
                                {
                                    var res = _sut.CallLow("low");
                                    Assert.Equal(0, res);
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }));
                            break;
                        }
                }
            }

            semaphore.Release(4);

            var waitTime = new TimeSpan(0, 6, 0);
            bool res = Task.WaitAll(tasks.ToArray(), waitTime);
            Assert.True(res, $"Failed to finish withing {waitTime.TotalMinutes} minutes");
        }
    }
}
