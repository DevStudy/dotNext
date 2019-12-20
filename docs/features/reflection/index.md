Advanced Reflection
====
.NEXT provides additional methods to reflect collection types, delegates and task types. These methods are located in `DotNext.Reflection` namespace.

# Collection
.NET Reflection contains a [method](https://docs.microsoft.com/en-us/dotnet/api/system.type.getelementtype) to obtain type of elements in the array. .NEXT provides special class [CollectionType](../../api/DotNext.Reflection.CollectionType.yml) to reflect type of collection elements.
```csharp
using DotNext.Reflection;

var itemType = typeof(List<string>).GetItemType();  //itemType == typeof(string)
```

This method ables to extract item type from any class implementing [IEnumerable&lt;T&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.collections.generic.ienumerable-1), even if such class is not generic class itself.

# Dispose pattern
[DisposableType](../../api/DotNext.Reflection.DisposableType.yml) allows to reflect `void Dispose()` method from any type. The reflection checks whether the class implements [IDisposable](https://docs.microsoft.com/en-us/dotnet/api/system.idisposable) method. If not, then it looks for the public instance parameterless method `Dispose` with **void** return type.

```csharp
using DotNext.Reflection;

var dispose = typeof(Stream).GetDisposeMethod();
```

# Tasks
[TaskType](../../api/DotNext.Reflection.TaskType.yml) provides a way to obtain actual generic argument of [Task](https://docs.microsoft.com/en-us/dotnet/api/system.threading.tasks.task-1). 
```csharp
using DotNext.Reflection;

var t = typeof(Task<int>).GetTaskType();    //t == typeof(int)
t = typeof(Task);   //t == typeof(void)
```

Additionally, it is possible to instantiate task type at runtime:
```csharp
using DotNext.Reflection;

var t = typeof(Task<>).MakeTaskType(typeof(string));    //t == typeof(Task<string>)
```