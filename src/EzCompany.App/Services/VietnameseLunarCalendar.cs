namespace EzCompany.App.Services;

public sealed record VietnameseLunarDate(
    int Day,
    int Month,
    int Year,
    bool IsLeapMonth,
    string CanChiYear,
    string DayOfWeekVietnamese,
    string FormattedSolar,
    string FormattedLunar);

/// <summary>
/// 100% offline Vietnamese Lunar Calendar converter (UTC+7 / Vietnam standard astronomical new moon
/// and solar longitude algorithm, Hồ Ngọc Đức method).
/// </summary>
public static class VietnameseLunarCalendar
{
    private const double VietnamTimeZone = 7.0;

    private static readonly string[] Can =
        ["Giáp", "Ất", "Bính", "Đinh", "Mậu", "Kỷ", "Canh", "Tân", "Nhâm", "Quý"];

    private static readonly string[] Chi =
        ["Tý", "Sửu", "Dần", "Mão", "Thìn", "Tỵ", "Ngọ", "Mùi", "Thân", "Dậu", "Tuất", "Hợi"];

    public static VietnameseLunarDate Today() => ConvertSolar2Lunar(DateOnly.FromDateTime(DateTime.Now));

    public static VietnameseLunarDate ConvertSolar2Lunar(DateOnly solar)
    {
        int dd = solar.Day;
        int mm = solar.Month;
        int yy = solar.Year;

        int dayNumber = JdFromDate(dd, mm, yy);
        int k = (int)((dayNumber - 2415021.076998695) / 29.530588853);
        int monthStart = GetNewMoonDay(k + 1, VietnamTimeZone);
        if (monthStart > dayNumber)
        {
            monthStart = GetNewMoonDay(k, VietnamTimeZone);
        }

        int a11 = GetLunarMonth11(yy, VietnamTimeZone);
        int b11 = a11;
        int lunarYear;
        if (a11 >= monthStart)
        {
            lunarYear = yy;
            a11 = GetLunarMonth11(yy - 1, VietnamTimeZone);
        }
        else
        {
            lunarYear = yy + 1;
            b11 = GetLunarMonth11(yy + 1, VietnamTimeZone);
        }

        int lunarDay = dayNumber - monthStart + 1;
        int diff = (monthStart - a11) / 29;
        bool lunarLeap = false;
        int lunarMonth = diff + 11;

        if (b11 - a11 > 365)
        {
            int leapMonthDiff = GetLeapMonthOffset(a11, VietnamTimeZone);
            if (diff >= leapMonthDiff)
            {
                lunarMonth = diff + 10;
                if (diff == leapMonthDiff)
                {
                    lunarLeap = true;
                }
            }
        }

        if (lunarMonth > 12)
        {
            lunarMonth -= 12;
        }

        if (lunarMonth >= 11 && diff < 4)
        {
            lunarYear -= 1;
        }

        string canChiYear = $"{Can[(lunarYear + 6) % 10]} {Chi[(lunarYear + 8) % 12]}";
        string dow = solar.DayOfWeek switch
        {
            DayOfWeek.Monday => "Thứ Hai",
            DayOfWeek.Tuesday => "Thứ Ba",
            DayOfWeek.Wednesday => "Thứ Tư",
            DayOfWeek.Thursday => "Thứ Năm",
            DayOfWeek.Friday => "Thứ Sáu",
            DayOfWeek.Saturday => "Thứ Bảy",
            _ => "Chủ Nhật"
        };

        string leapSuffix = lunarLeap ? " (nhuận)" : string.Empty;
        string formattedSolar = $"{dow}, ngày {dd:D2}/{mm:D2}/{yy}";
        string formattedLunar = $"Ngày {lunarDay:D2} tháng {lunarMonth:D2}{leapSuffix} năm {canChiYear} ({lunarYear})";

        return new VietnameseLunarDate(
            lunarDay,
            lunarMonth,
            lunarYear,
            lunarLeap,
            canChiYear,
            dow,
            formattedSolar,
            formattedLunar);
    }

    private static int JdFromDate(int dd, int mm, int yy)
    {
        int a = (14 - mm) / 12;
        int y = yy + 4800 - a;
        int m = mm + 12 * a - 3;
        int jd = dd + (153 * m + 2) / 5 + 365 * y + y / 4 - y / 100 + y / 400 - 32045;
        if (jd < 2299161)
        {
            jd = dd + (153 * m + 2) / 5 + 365 * y + y / 4 - 32083;
        }

        return jd;
    }

    private static int GetNewMoonDay(int k, double timeZone)
    {
        double t = k / 1236.85;
        double t2 = t * t;
        double t3 = t2 * t;
        double dr = Math.PI / 180.0;
        double jd1 = 2415020.75933 + 29.53058868 * k + 0.0001178 * t2 - 0.000000155 * t3;
        jd1 += 0.00033 * Math.Sin((166.56 + 132.87 * t - 0.009173 * t2) * dr);

        double m = 359.2242 + 29.10535608 * k - 0.0000333 * t2 - 0.00000347 * t3;
        double mpr = 306.0253 + 385.81691806 * k + 0.0107306 * t2 + 0.00001236 * t3;
        double f = 21.2964 + 390.67050646 * k - 0.0016528 * t2 - 0.00000239 * t3;

        double c1 = (0.1734 - 0.000393 * t) * Math.Sin(m * dr) + 0.0021 * Math.Sin(2 * dr * m);
        c1 = c1 - 0.4068 * Math.Sin(mpr * dr) + 0.0161 * Math.Sin(dr * 2 * mpr);
        c1 -= 0.0004 * Math.Sin(dr * 3 * mpr);
        c1 = c1 + 0.0104 * Math.Sin(dr * 2 * f) - 0.0051 * Math.Sin(dr * (m + mpr));
        c1 = c1 - 0.0074 * Math.Sin(dr * (m - mpr)) + 0.0004 * Math.Sin(dr * (2 * f + m));
        c1 = c1 - 0.0004 * Math.Sin(dr * (2 * f - m)) - 0.0006 * Math.Sin(dr * (2 * f + mpr));
        c1 = c1 + 0.0010 * Math.Sin(dr * (2 * f - mpr)) + 0.0005 * Math.Sin(dr * (2 * mpr + m));

        double deltaT = t < -11
            ? 0.001 + 0.000839 * t + 0.0002261 * t2 - 0.00000845 * t3 - 0.000000081 * t * t3
            : -0.000278 + 0.000265 * t + 0.000262 * t2;

        double jdNew = jd1 + c1 - deltaT;
        return (int)Math.Floor(jdNew + 0.5 + timeZone / 24.0);
    }

    private static int GetSunLongitude(int jdn, double timeZone)
    {
        double t = (jdn - 0.5 - timeZone / 24.0 - 2451545.0) / 36525.0;
        double t2 = t * t;
        double dr = Math.PI / 180.0;
        double m = 357.52910 + 35999.05030 * t - 0.0001559 * t2 - 0.00000048 * t * t2;
        double l0 = 280.46645 + 36000.76983 * t + 0.0003032 * t2;
        double dl = (1.914600 - 0.004817 * t - 0.000014 * t2) * Math.Sin(dr * m);
        dl = dl + (0.019993 - 0.000101 * t) * Math.Sin(dr * 2 * m) + 0.000290 * Math.Sin(dr * 3 * m);
        double l = (l0 + dl) * dr;
        l -= Math.PI * 2 * Math.Floor(l / (Math.PI * 2));
        return (int)Math.Floor(l / Math.PI * 6.0);
    }

    private static int GetLunarMonth11(int yy, double timeZone)
    {
        double off = JdFromDate(31, 12, yy) - 2415021.0;
        int k = (int)Math.Floor(off / 29.530588853);
        int nm = GetNewMoonDay(k, timeZone);
        int sunLong = GetSunLongitude(nm, timeZone);
        if (sunLong >= 9)
        {
            nm = GetNewMoonDay(k - 1, timeZone);
        }

        return nm;
    }

    private static int GetLeapMonthOffset(int a11, double timeZone)
    {
        int k = (int)Math.Floor((a11 - 2415021.076998695) / 29.530588853 + 0.5);
        int last;
        int i = 1;
        int arc = GetSunLongitude(GetNewMoonDay(k + i, timeZone), timeZone);
        do
        {
            last = arc;
            i++;
            arc = GetSunLongitude(GetNewMoonDay(k + i, timeZone), timeZone);
        } while (arc != last && i < 14);

        return i - 1;
    }
}
