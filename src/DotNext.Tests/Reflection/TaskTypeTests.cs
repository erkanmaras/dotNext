﻿using System.Diagnostics.CodeAnalysis;

namespace DotNext.Reflection
{
    [ExcludeFromCodeCoverage]
    public sealed class TaskTypeTests : Test
    {
        [Fact]
        public static void ReflectTaskType()
        {
            var task = typeof(Task);
            Equal(typeof(void), task.GetTaskType());
            Equal(typeof(Task), typeof(void).MakeTaskType());
            task = typeof(Task<int>);
            Equal(typeof(int), task.GetTaskType());
            Equal(typeof(Task<int>), typeof(int).MakeTaskType());
        }

        [Fact]
        public static void ReflectValueTaskType()
        {
            var task = typeof(ValueTask);
            Equal(typeof(void), task.GetTaskType());
            Equal(typeof(ValueTask), typeof(void).MakeTaskType(true));
            task = typeof(ValueTask<int>);
            Equal(typeof(int), task.GetTaskType());
            Equal(typeof(ValueTask<int>), typeof(int).MakeTaskType(true));
        }

        [Fact]
        public static void IsCompletedSuccessfullyPropertyGetter()
        {
            var task = Task.CompletedTask;
            True(TaskType.IsCompletedSuccessfullyGetter(task));
        }

        [Fact]
        public static void GetResultSynchronously()
        {
            var task = Task.FromResult(42);
            Equal(42, TaskType.GetResultGetter<int>().Invoke(task));
        }
    }
}
