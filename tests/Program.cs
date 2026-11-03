using System;
using MondayCassettePack;

for (int day = 1; day <= 10000; day++)
{
    int due = DeliverySchedule.NextDay(day);
    if (due != day + 1)
        throw new Exception("Incorrect next day for " + day);
    if (DeliverySchedule.IsDue(day, due, false)) throw new Exception("Early delivery");
    if (!DeliverySchedule.IsDue(due, due, false)) throw new Exception("Missed due day");
    if (!DeliverySchedule.IsDue(due + 7, due, false)) throw new Exception("Missed retry");
    if (DeliverySchedule.IsDue(due, due, true)) throw new Exception("Duplicate receipt");
    if (!DeliverySchedule.IsDue(due + 1, due, false)) throw new Exception("Missed next-day retry");
}
if (DeliverySchedule.IsDue(8, 0, false)) throw new Exception("Uninitialized schedule");
Console.WriteLine("Passed: 10,000 starting days, next-day delivery, retries and duplicate suppression.");
