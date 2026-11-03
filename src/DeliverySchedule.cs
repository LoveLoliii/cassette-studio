namespace MondayCassettePack;

public static class DeliverySchedule
{
    public static int NextDay(int day) => day + 1;

    // Retry on any later day if delivery was missed.
    public static bool IsDue(int day, int scheduledDay, bool delivered) =>
        !delivered && scheduledDay > 0 && day >= scheduledDay;
}
