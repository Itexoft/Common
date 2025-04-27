// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Core;

public delegate TValue GetFunc<TValue>(Func<TValue> func);

public delegate void SetAction<out TValue>(Action<TValue> action);

public delegate ValueTask<TValue> GetValueTaskFunc<TValue>(Func<ValueTask<TValue>> func);

public delegate ValueTask SetValueTaskAction<out TValue>(Func<TValue, ValueTask> action);

public delegate Task<TValue> GetTaskFunc<TValue>(Func<Task<TValue>> func);

public delegate Task SetTaskAction<out TValue>(Func<TValue, Task> action);

public delegate bool TryGetFunc<TValue>(Func<bool> func, out TValue value);

public delegate bool TrySetAction<out TValue>(Action<TValue> action);

public delegate ValueTask<bool> TrySetValueTaskAction<out TValue>(Func<TValue, ValueTask<bool>> action);

public delegate TValue GetFunc<TValue, TArg1>(Func<TArg1, TValue> func, TArg1 arg1);

public delegate void SetAction<out TValue, TArg1>(Action<TArg1, TValue> action, TArg1 arg1);

public delegate ValueTask<TValue> GetValueTaskFunc<TValue, TArg1>(Func<TArg1, ValueTask<TValue>> func, TArg1 arg1);

public delegate ValueTask SetValueTaskAction<out TValue, TArg1>(Func<TArg1, TValue, ValueTask> action, TArg1 arg1);

public delegate Task<TValue> GetTaskFunc<TValue, TArg1>(Func<TArg1, Task<TValue>> func, TArg1 arg1);

public delegate Task SetTaskAction<out TValue, TArg1>(Func<TArg1, TValue, Task> action, TArg1 arg1);

public delegate TValue GetFunc<TValue, TArg1, TArg2>(Func<TArg1, TArg2, TValue> func, TArg1 arg1, TArg2 arg2);

public delegate void SetAction<out TValue, TArg1, TArg2>(Action<TArg1, TArg2, TValue> action, TArg1 arg1, TArg2 arg2);

public delegate ValueTask<TValue> GetValueTaskFunc<TValue, TArg1, TArg2>(Func<TArg1, TArg2, ValueTask<TValue>> func, TArg1 arg1, TArg2 arg2);

public delegate ValueTask SetValueTaskAction<out TValue, TArg1, TArg2>(Func<TArg1, TArg2, TValue, ValueTask> action, TArg1 arg1, TArg2 arg2);

public delegate Task<TValue> GetTaskFunc<TValue, TArg1, TArg2>(Func<TArg1, TArg2, Task<TValue>> func, TArg1 arg1, TArg2 arg2);

public delegate Task SetTaskAction<out TValue, TArg1, TArg2>(Func<TArg1, TArg2, TValue, Task> action, TArg1 arg1, TArg2 arg2);
