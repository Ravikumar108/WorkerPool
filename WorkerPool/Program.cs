using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.Threading;
using System.Collections.Concurrent;

namespace WorkerPool
{
    public class _WorkerPool : IDisposable
    {
        class ProcessItem // Class represent a unit to process
        {
            public readonly TaskCompletionSource<object> TaskSource; // Responsible for ways Task can end
            public readonly Action Action; // Code to execute
            public readonly CancellationTokenSource CancelToken; // Token to cancel a task
            public readonly int _miliSecTimeOut;

            public ProcessItem(TaskCompletionSource<object> taskSource, Action action,
                CancellationTokenSource cancelToken, int miliSecTimeOut)
            {
                TaskSource = taskSource;
                Action = action;
                CancelToken = cancelToken;
                _miliSecTimeOut = miliSecTimeOut;
            }
        }
        // Thread safe collection to hold ProcessItems
        BlockingCollection<ProcessItem> _taskQ = new BlockingCollection<ProcessItem>();

        public _WorkerPool(int workerCount, CancellationTokenSource tokenF)
        {
            // Create and start a separate Task (Thread) for N consumer:
            for (int i = 0; i < workerCount; i++)
                Task.Factory.StartNew(Consume, tokenF.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void Dispose() { _taskQ.CompleteAdding(); }

        public Task EnqueueTask(Action action)
        {
            return EnqueueTask(action, null, 0);
        }

        public Task EnqueueTask(Action action, CancellationTokenSource cancelTokenObj, int nTimeOutMsec)
        {
            var tcs = new TaskCompletionSource<object>();
            _taskQ.Add(new ProcessItem(tcs, action, cancelTokenObj, nTimeOutMsec));
            return tcs.Task;
        }
        // Consume each ProcessItem added to the queue;
        // Called automatically when item is added to the BlockingCollection
        void Consume()
        {
            foreach (ProcessItem ProcessItem in _taskQ.GetConsumingEnumerable())
            {
                // Get Item to process; just check if it's been marked cancel
                if (ProcessItem.CancelToken.Token.IsCancellationRequested)
                {
                    ProcessItem.TaskSource.SetCanceled();
                }
                else // Go ahead and process
                {
                    try
                    {
                        if (ProcessItem.CancelToken != null)
                            ProcessItem.CancelToken.CancelAfter(ProcessItem._miliSecTimeOut); // Set process time out in msec
                        ProcessItem.Action();
                        //IAsyncAction asyncState = Task.Factory.StartNew(() => ProcessItem.Action(),
                        //    ProcessItem.CancelToken.Token);
                        //t.RunSynchronously();
                        //t.Start();
                        if (ProcessItem.CancelToken.Token.IsCancellationRequested)
                            throw new OperationCanceledException();
                        //t.Wait(ProcessItem.CancelToken.Token);
                        //t.Start();
                        ProcessItem.TaskSource.SetResult(null);   // Indicate completion
                    }
                    catch (OperationCanceledException ex) // Cancelled or Threw Exception while executing
                    {
                        // Cancelled either by user or timeed out
                        if (ex.CancellationToken == ProcessItem.CancelToken.Token)
                            ProcessItem.TaskSource.SetCanceled();
                        else
                            ProcessItem.TaskSource.SetException(ex);
                    }
                    catch (Exception ex)
                    {
                        ProcessItem.TaskSource.SetException(ex);
                    }
                }
            }
        }
    } // Process Consumer
    class Program
    {
        // Token to cancel all
        static CancellationTokenSource tokenSourceQ = new CancellationTokenSource();
        // Initialize WorkerPool of desired size.
        static _WorkerPool WorkerPool = new _WorkerPool(4, tokenSourceQ);
        static void Main(string[] args)
        {
            try
            {
                return AsyncContext.Run(() => MainAsync(args));
            }
            catch (Exception ex) { }
            // Token to cancel individual process items
            CancellationTokenSource tokenToCancelEach = new CancellationTokenSource();
            Task t = WorkerPool.EnqueueTask(() => Exec(new Object()), tokenToCancelEach, 1000);
            t.Wait();
            //tokenToCancelEach.Cancel();
        }
        static async Task<int> MainAsync(string[] args)
        {
            await Task.Delay(100);
            return 1;
        }
        public static void Exec(Object paramObj)
        {
            Thread.Sleep(2000);
        }
        
    }
}
