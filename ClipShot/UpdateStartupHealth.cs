namespace ClipShot;

// ready подтверждает создание окна, но не доказывает, что пережиты отложенные
// обработчики запуска (включая плашку обновления). Нужны оба условия.
internal static class UpdateStartupHealth
{
    internal static async Task<bool> WaitAsync(Func<bool> ready,Func<bool> exited,Func<TimeSpan> elapsed,Func<Task> delay)
    {
        TimeSpan? readyAt=null;
        while(elapsed()<TimeSpan.FromSeconds(60))
        {
            if(exited())return false;
            if(ready())readyAt??=elapsed();
            if(readyAt is {} start&&elapsed()-start>=TimeSpan.FromSeconds(10))return true;
            await delay();
        }
        return false;
    }
}
