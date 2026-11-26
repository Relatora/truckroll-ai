using Truckroll.Data.Domain;

namespace Truckroll.Data.Seed;

/// <summary>
/// Builds Kestrel Home Comfort: branches, technicians, about 300 customers with their homes and
/// equipment, two years of job history, today's board, the next few days, open calls and the
/// knowledge base.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deterministic.</b> The same seed and anchor date always produce identical data: one
/// <see cref="Random"/>, consumed in a fixed order, and no clock reads. Tests and evals rely on it.
/// </para>
/// <para>
/// <b>Relative to the anchor date.</b> History runs back from it and the board is built on it,
/// so the demo always has a live day whenever it's run.
/// </para>
/// <para>
/// <b>A frozen moment.</b> The board is a snapshot of late morning: morning jobs are done, late-morning
/// jobs are on site, afternoon jobs are assigned or waiting for a dispatcher.
/// </para>
/// </remarks>
public sealed class DemoDataGenerator
{
    /// <summary>The company's time zone. Appointment windows are local; storage is UTC.</summary>
    public static readonly TimeZoneInfo LocalZone = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

    private static readonly (int Start, int End)[] Windows = [(8, 10), (10, 12), (12, 14), (14, 16)];

    private static readonly Dictionary<string, string[]> Sizes = new()
    {
        ["CAL-80"] = ["60", "80", "100"],
        ["CAL-96"] = ["60", "80", "100"],
        ["HM-95"] = ["60", "80"],
        ["FL-16"] = ["24", "30", "36"],
        ["FHP-20"] = ["24", "36", "48"],
    };

    private static readonly string[] EmailDomains = ["mail.test", "inbox.test", "example.test"];

    private readonly Random _random;
    private readonly DateOnly _today;
    private readonly DemoData _data = new();
    private readonly Queue<string> _phonePool;
    private readonly Dictionary<string, Branch> _branches = [];
    private int _serialCounter = 10_000;

    /// <summary>Technician windows already taken today and ahead, so no truck is in two places at once.</summary>
    private readonly HashSet<(Technician Technician, DateOnly Day, int Hour)> _booked = [];

    /// <summary>"street|town" pairs already handed out, so no two homes share an address.</summary>
    private readonly HashSet<string> _addresses = new(StringComparer.OrdinalIgnoreCase);

    public DemoDataGenerator(int seed, DateOnly today)
    {
        _random = new Random(seed);
        _today = today;
        _phonePool = BuildPhonePool();
    }

    /// <summary>Generates the whole company. Call once per instance.</summary>
    public DemoData Generate()
    {
        AddBranchesAndTechnicians();
        AddAnchorCustomers();
        AddGeneratedCustomers();
        AddHistory();
        AddTodayAndUpcoming();
        NumberJobs();

        _data.Documents.AddRange(SeedDocuments.Load(_today, _branches));
        _data.Calls.AddRange(SeedCalls.Load(_today, _branches, _data.Customers));

        return _data;
    }

    // ---------------------------------------------------------------- branches and people

    private void AddBranchesAndTechnicians()
    {
        foreach (var seed in PlaceAndPeopleCatalog.Branches)
        {
            var branch = new Branch
            {
                Code = seed.Code,
                Name = seed.Name,
                Street = seed.Street,
                City = seed.Name,
                Phone = seed.Phone,
                Latitude = seed.Latitude,
                Longitude = seed.Longitude,
            };
            _branches[seed.Code] = branch;
            _data.Branches.Add(branch);
        }

        foreach (var seed in PlaceAndPeopleCatalog.Technicians)
        {
            _data.Technicians.Add(new Technician
            {
                Branch = _branches[seed.BranchCode],
                FirstName = seed.FirstName,
                LastName = seed.LastName,
                Skills = seed.Skills,
                Certifications = seed.Certifications,
                Color = seed.Color,
                HiredOn = new DateOnly(seed.HiredYear, 3 + (seed.FirstName.Length % 8), 1),
            });
        }
    }

    private void AddAnchorCustomers()
    {
        foreach (var anchor in AnchorCustomers.All)
        {
            var branch = _branches[anchor.BranchCode];
            var customer = new Customer
            {
                Branch = branch,
                FirstName = anchor.FirstName,
                LastName = anchor.LastName,
                Email = anchor.Email,
                Phone = anchor.Phone,
                Source = anchor.Source,
                CreatedAt = AtLocal(_today.AddDays(-anchor.Equipment.Max(e => e.InstalledDaysAgo) - 14), 11),
            };
            var property = new Property
            {
                Customer = customer,
                Street = Claim(anchor.Street, branch.Name),
                City = branch.Name,
                PostalCode = anchor.PostalCode,
                Latitude = anchor.Latitude,
                Longitude = anchor.Longitude,
                Kind = anchor.Kind,
                YearBuilt = anchor.YearBuilt,
                AccessNotes = anchor.AccessNotes,
            };
            customer.Properties.Add(property);

            var equipment = anchor.Equipment
                .Select(e => NewEquipment(property, EquipmentCatalog.ByCode(e.ModelCode), e.Size, _today.AddDays(-e.InstalledDaysAgo), e.InstalledByUs, e.SerialSuffix))
                .ToList();

            if (anchor.Membership is { } plan)
            {
                customer.Membership = NewMembership(plan, _today.AddDays(-700), active: true);
            }

            _data.Customers.Add(customer);
            _data.AnchorCustomers.Add(customer);

            foreach (var job in anchor.History)
            {
                var item = equipment[job.Equipment];
                var model = EquipmentCatalog.ByCode(ModelCode(item));
                var technician = QualifiedTechnician(_branches[job.TechnicianBranch], model.Certification, model.Skill)
                    ?? throw new InvalidOperationException($"No technician in {job.TechnicianBranch} can work on {model.Code}.");
                AddCompletedRepair(customer, property, item, model.Faults[job.Fault], technician, _today.AddDays(-job.DaysAgo), forceFirstTimeFix: true);
            }
        }
    }

    private void AddGeneratedCustomers()
    {
        foreach (var seed in PlaceAndPeopleCatalog.Branches)
        {
            var branch = _branches[seed.Code];
            for (var i = 0; i < seed.Customers; i++)
            {
                _data.Customers.Add(NewGeneratedCustomer(branch));
            }
        }
    }

    private Customer NewGeneratedCustomer(Branch branch)
    {
        var first = Pick(PlaceAndPeopleCatalog.FirstNames);
        var last = Pick(PlaceAndPeopleCatalog.LastNames);
        var customer = new Customer
        {
            Branch = branch,
            FirstName = first,
            LastName = last,
            Email = $"{first}.{last.Replace(" ", "", StringComparison.Ordinal)}{_random.Next(10, 99)}@{Pick(EmailDomains)}".ToLowerInvariant(),
            Phone = _phonePool.Dequeue(),
            Source = PickWeighted(
                (CustomerSource.Referral, 5), (CustomerSource.GoogleSearch, 6), (CustomerSource.GoogleReviews, 4),
                (CustomerSource.Facebook, 2), (CustomerSource.YardSign, 1), (CustomerSource.TruckWrap, 1),
                (CustomerSource.HomeShow, 1), (CustomerSource.RepeatCustomer, 3)),
            CreatedAt = AtLocal(_today.AddDays(-_random.Next(30, 6 * 365)), 9 + _random.Next(8)),
        };

        var yearBuilt = PickWeighted((_random.Next(1950, 1980), 3), (_random.Next(1980, 2005), 4), (_random.Next(2005, 2024), 3));
        var property = new Property
        {
            Customer = customer,
            Street = UniqueStreet(branch.Name),
            City = branch.Name,
            PostalCode = RandomPostalCode(),
            Latitude = branch.Latitude + (_random.NextDouble() - 0.5) * 0.08,
            Longitude = branch.Longitude + (_random.NextDouble() - 0.5) * 0.11,
            Kind = PickWeighted((PropertyKind.Detached, 6), (PropertyKind.SemiDetached, 2), (PropertyKind.Townhouse, 2), (PropertyKind.Bungalow, 2), (PropertyKind.Cottage, 1)),
            YearBuilt = yearBuilt,
        };
        customer.Properties.Add(property);

        AddGeneratedEquipment(property, yearBuilt);

        if (_random.NextDouble() < 0.35)
        {
            var started = _today.AddDays(-_random.Next(60, 4 * 365));
            customer.Membership = NewMembership(
                _random.NextDouble() < 0.7 ? MembershipPlan.ComfortClub : MembershipPlan.ComfortClubPlus,
                started,
                active: _random.NextDouble() < 0.82);
        }

        return customer;
    }

    private void AddGeneratedEquipment(Property property, int yearBuilt)
    {
        // Equipment can't predate the house, and nothing here is older than about 20 years.
        var earliest = new DateOnly(Math.Max(yearBuilt, _today.Year - 20), 1, 1);
        DateOnly InstallDate() => RandomDateBetween(earliest, _today.AddDays(-30));

        if (_random.NextDouble() < 0.9)
        {
            var installed = InstallDate();
            // Calderon's mid-efficiency CAL-80 sold heavily in 2019 to 2021, which is what makes
            // the ignitor bulletin (SB-2024-07) matter to this customer base.
            var model = installed.Year is >= 2019 and <= 2021 && _random.NextDouble() < 0.55
                ? EquipmentCatalog.ByCode("CAL-80")
                : PickWeighted((EquipmentCatalog.ByCode("CAL-80"), 2), (EquipmentCatalog.ByCode("CAL-96"), 5), (EquipmentCatalog.ByCode("HM-95"), 3));
            NewEquipment(property, model, PickSize(model), installed, _random.NextDouble() < 0.65);
        }
        else
        {
            NewEquipment(property, EquipmentCatalog.ByCode("HMB-110"), "", InstallDate(), _random.NextDouble() < 0.65);
        }

        var cooling = _random.NextDouble();
        if (cooling < 0.72)
        {
            var model = EquipmentCatalog.ByCode("FL-16");
            NewEquipment(property, model, PickSize(model), InstallDate(), _random.NextDouble() < 0.65);
        }
        else if (cooling < 0.84)
        {
            var model = EquipmentCatalog.ByCode("FHP-20");
            NewEquipment(property, model, PickSize(model), RandomDateBetween(_today.AddYears(-6), _today.AddDays(-30)), true);
        }

        var water = _random.NextDouble();
        if (water < 0.68)
        {
            NewEquipment(property, EquipmentCatalog.ByCode("AV-50"), "", InstallDate(), _random.NextDouble() < 0.5);
        }
        else if (water < 0.9)
        {
            NewEquipment(property, EquipmentCatalog.ByCode("AVT-199"), "", RandomDateBetween(_today.AddYears(-8), _today.AddDays(-30)), true);
        }

        if (_random.NextDouble() < 0.25)
        {
            NewEquipment(property, EquipmentCatalog.ByCode("MH-12"), "", InstallDate(), true);
        }

        if (_random.NextDouble() < 0.15)
        {
            NewEquipment(property, EquipmentCatalog.ByCode("BW-150"), "", InstallDate(), true);
        }
    }

    private Equipment NewEquipment(Property property, EquipmentModel model, string size, DateOnly installedOn, bool installedByUs, string? serialSuffix = null)
    {
        var suffix = serialSuffix ?? (_serialCounter++).ToString("00000", System.Globalization.CultureInfo.InvariantCulture);
        var equipment = new Equipment
        {
            Property = property,
            Kind = model.Kind,
            Brand = model.Brand,
            Model = size.Length == 0 ? model.Code : string.Format(System.Globalization.CultureInfo.InvariantCulture, model.ModelPattern, size),
            SerialNumber = $"{model.Code.Replace("-", "", StringComparison.Ordinal)}-{installedOn.Year % 100:00}-{suffix}",
            InstalledOn = installedOn,
            // We know the warranty on what we installed. On other companies' installs, about half
            // the time the homeowner had the paperwork.
            WarrantyExpiresOn = installedByUs || _random.NextDouble() < 0.5 ? installedOn.AddYears(model.WarrantyYears) : null,
            InstalledByUs = installedByUs,
        };
        property.Equipment.Add(equipment);
        return equipment;
    }

    private Membership NewMembership(MembershipPlan plan, DateOnly started, bool active)
    {
        // Renews yearly on its start date: the next anniversary for active plans, a past one
        // for lapsed or cancelled plans.
        var renews = started.AddYears(1);
        while (renews <= _today)
        {
            renews = renews.AddYears(1);
        }

        if (!active)
        {
            renews = renews.AddYears(-1);
        }

        return new Membership
        {
            Plan = plan,
            Status = active ? MembershipStatus.Active : PickWeighted((MembershipStatus.Lapsed, 2), (MembershipStatus.Cancelled, 1)),
            StartedOn = started,
            RenewsOn = renews,
            MonthlyPrice = plan == MembershipPlan.ComfortClub ? 19.99m : 29.99m,
        };
    }

    // ---------------------------------------------------------------- history

    private void AddHistory()
    {
        var historyStart = _today.AddYears(-2);

        foreach (var customer in _data.Customers.Except(_data.AnchorCustomers).ToList())
        {
            var property = customer.Properties[0];
            foreach (var equipment in property.Equipment.ToList())
            {
                var model = EquipmentCatalog.ByCode(ModelCode(equipment));

                if (equipment.InstalledByUs && equipment.InstalledOn > historyStart)
                {
                    AddInstallation(customer, property, equipment, model);
                }

                var repairs = PickWeighted((0, 5), (1, 4), (2, 2));
                // The planted pattern: CAL-80s from 2019 to 2021 lose ignitors over and over.
                if (model.Code == "CAL-80" && equipment.InstalledOn.Year is >= 2019 and <= 2021)
                {
                    repairs += 1;
                }

                for (var i = 0; i < repairs; i++)
                {
                    var fault = PickWeighted(model.Faults.Select(f => (f, f.Weight)).ToArray());
                    // History stops five days back: a repair that needs a return visit books it up to
                    // three days later, and a completed visit can't be in the future.
                    var day = RandomDayInMonths(Later(historyStart, equipment.InstalledOn.AddDays(60)), _today.AddDays(-5), fault.Months);
                    if (day is null)
                    {
                        continue;
                    }

                    var technician = QualifiedTechnician(customer.Branch, model.Certification, model.Skill);
                    if (technician is not null)
                    {
                        AddCompletedRepair(customer, property, equipment, fault, technician, day.Value, forceFirstTimeFix: false);
                    }
                }
            }

            if (customer.Membership is { Status: MembershipStatus.Active })
            {
                AddTuneUps(customer, property, historyStart);
            }
        }
    }

    private void AddCompletedRepair(Customer customer, Property property, Equipment equipment, Fault fault, Technician technician, DateOnly day, bool forceFirstTimeFix)
    {
        var window = Pick(Windows);
        var firstTimeFix = forceFirstTimeFix || _random.NextDouble() < fault.FirstTimeFix;
        var start = AtLocal(day, window.Start);
        var job = new Job
        {
            Number = "",
            Branch = customer.Branch,
            Customer = customer,
            Property = property,
            Equipment = equipment,
            Type = JobType.Repair,
            Priority = fault.Months.Length < 12 && _random.NextDouble() < 0.3 ? JobPriority.Emergency : PickWeighted((JobPriority.Urgent, 2), (JobPriority.Normal, 3)),
            Status = JobStatus.Completed,
            RequiredSkill = EquipmentCatalog.ByCode(ModelCode(equipment)).Skill,
            RequiredCertification = EquipmentCatalog.ByCode(ModelCode(equipment)).Certification,
            WindowStart = start,
            WindowEnd = AtLocal(day, window.End),
            EstimatedMinutes = fault.Minutes,
            Technician = technician,
            Summary = fault.Symptom,
            Resolution = firstTimeFix ? fault.Resolution : $"Diagnosed: {fault.Resolution} Part not on the truck; return visit booked.",
            InvoiceTotal = Money(fault.Low, fault.High),
            FirstTimeFix = firstTimeFix,
            CreatedAt = start.AddDays(-_random.Next(0, 3)).AddHours(-3),
            DispatchedAt = start.AddHours(-1),
            CompletedAt = start.AddMinutes(fault.Minutes + 30),
        };
        job.Notes.Add(new JobNote { Author = technician.FullName, Source = NoteSource.Technician, Body = fault.Note, CreatedAt = job.CompletedAt.Value });
        _data.Jobs.Add(job);

        if (!firstTimeFix)
        {
            var returnDay = day.AddDays(_random.Next(1, 4));
            var returnStart = AtLocal(returnDay, 8);
            var visit = new Job
            {
                Number = "",
                Branch = customer.Branch,
                Customer = customer,
                Property = property,
                Equipment = equipment,
                Type = JobType.Repair,
                Priority = JobPriority.Urgent,
                Status = JobStatus.Completed,
                RequiredSkill = job.RequiredSkill,
                RequiredCertification = job.RequiredCertification,
                WindowStart = returnStart,
                WindowEnd = AtLocal(returnDay, 10),
                EstimatedMinutes = 45,
                Technician = technician,
                Summary = $"Return visit with parts: {fault.Symptom.ToLowerInvariant()}",
                Resolution = fault.Resolution,
                InvoiceTotal = 0m,
                FirstTimeFix = true,
                CreatedAt = job.CompletedAt!.Value,
                DispatchedAt = returnStart.AddHours(-1),
                CompletedAt = returnStart.AddMinutes(75),
            };
            visit.Notes.Add(new JobNote { Author = technician.FullName, Source = NoteSource.Technician, Body = "Installed the part ordered on the first visit. Tested, working.", CreatedAt = visit.CompletedAt.Value });
            _data.Jobs.Add(visit);
        }
    }

    private void AddInstallation(Customer customer, Property property, Equipment equipment, EquipmentModel model)
    {
        var technician = QualifiedTechnician(customer.Branch, model.Certification, TechnicianSkills.Installations)
            ?? QualifiedTechnician(customer.Branch, model.Certification, model.Skill);
        if (technician is null)
        {
            return;
        }

        var start = AtLocal(equipment.InstalledOn, 8);
        var job = new Job
        {
            Number = "",
            Branch = customer.Branch,
            Customer = customer,
            Property = property,
            Equipment = equipment,
            Type = JobType.Installation,
            Priority = JobPriority.Normal,
            Status = JobStatus.Completed,
            RequiredSkill = TechnicianSkills.Installations,
            RequiredCertification = model.Certification,
            WindowStart = start,
            WindowEnd = AtLocal(equipment.InstalledOn, 10),
            EstimatedMinutes = 360,
            Technician = technician,
            Summary = $"Install new {model.Brand} {equipment.Model}.",
            Resolution = $"Installed {model.Brand} {equipment.Model}, serial {equipment.SerialNumber}. Commissioned and walked the homeowner through it.",
            InvoiceTotal = model.Kind switch
            {
                EquipmentKind.Furnace or EquipmentKind.Boiler or EquipmentKind.HeatPump => Money(5200, 9800),
                EquipmentKind.AirConditioner => Money(4100, 6900),
                EquipmentKind.WaterHeater or EquipmentKind.TanklessWaterHeater => Money(1900, 4200),
                _ => Money(650, 1400),
            },
            FirstTimeFix = true,
            CreatedAt = start.AddDays(-_random.Next(7, 30)),
            DispatchedAt = start.AddDays(-1),
            CompletedAt = start.AddHours(7),
        };
        job.Notes.Add(new JobNote { Author = technician.FullName, Source = NoteSource.Technician, Body = $"Install complete. Old unit hauled away. Registered the {model.Brand} warranty online.", CreatedAt = job.CompletedAt.Value });
        _data.Jobs.Add(job);
    }

    private void AddTuneUps(Customer customer, Property property, DateOnly historyStart)
    {
        // Members get a heating tune-up each fall and a cooling one each spring.
        var heating = property.Equipment.FirstOrDefault(e => e.Kind is EquipmentKind.Furnace or EquipmentKind.Boiler);
        var cooling = property.Equipment.FirstOrDefault(e => e.Kind is EquipmentKind.AirConditioner or EquipmentKind.HeatPump);

        for (var year = historyStart.Year; year <= _today.Year; year++)
        {
            if (heating is not null)
            {
                AddTuneUp(customer, property, heating, new DateOnly(year, 10, 1), historyStart);
            }

            if (cooling is not null)
            {
                AddTuneUp(customer, property, cooling, new DateOnly(year, 4, 15), historyStart);
            }
        }
    }

    private void AddTuneUp(Customer customer, Property property, Equipment equipment, DateOnly seasonStart, DateOnly historyStart)
    {
        var day = seasonStart.AddDays(_random.Next(0, 40));
        if (day < historyStart || day >= _today.AddDays(-1) || day < equipment.InstalledOn)
        {
            return;
        }

        var model = EquipmentCatalog.ByCode(ModelCode(equipment));
        var technician = QualifiedTechnician(customer.Branch, model.Certification, model.Skill);
        if (technician is null)
        {
            return;
        }

        var window = Pick(Windows);
        var start = AtLocal(day, window.Start);
        var job = new Job
        {
            Number = "",
            Branch = customer.Branch,
            Customer = customer,
            Property = property,
            Equipment = equipment,
            Type = JobType.Maintenance,
            Priority = JobPriority.Normal,
            Status = JobStatus.Completed,
            RequiredSkill = model.Skill,
            RequiredCertification = model.Certification,
            WindowStart = start,
            WindowEnd = AtLocal(day, window.End),
            EstimatedMinutes = 60,
            Technician = technician,
            Summary = $"Comfort Club tune-up: {Describe(equipment.Kind)}.",
            Resolution = "Seasonal maintenance per checklist. Filter replaced, safeties tested, readings recorded.",
            InvoiceTotal = 0m,
            FirstTimeFix = true,
            CreatedAt = start.AddDays(-14),
            DispatchedAt = start.AddHours(-1),
            CompletedAt = start.AddMinutes(70),
        };
        _data.Jobs.Add(job);
    }

    // ---------------------------------------------------------------- today and the days ahead

    private void AddTodayAndUpcoming()
    {
        AddTheDemoEmergency();

        // Keep Nadia free at noon: she's who the emergency's dispatch suggestion should find.
        _booked.Add((_data.Technicians.Single(t => t.FirstName == "Nadia"), _today, 12));

        foreach (var branch in _data.Branches)
        {
            var customers = _data.Customers.Where(c => c.Branch == branch && !_data.AnchorCustomers.Contains(c)).ToList();
            var technicians = _data.Technicians.Where(t => t.Branch == branch).ToList();

            for (var day = 0; day <= 5; day++)
            {
                var date = _today.AddDays(day);
                if (date.DayOfWeek == DayOfWeek.Sunday)
                {
                    continue;
                }

                var count = day == 0 ? technicians.Count * 3 : technicians.Count * 2 + _random.Next(0, 3);
                for (var i = 0; i < count; i++)
                {
                    var window = Windows[i % Windows.Length];

                    // Today's morning is built around the technicians: each one gets an 8 am job
                    // (done) and a 10 am job (on site) on equipment they're licensed for, so every
                    // branch's board shows a real morning whatever the dice say.
                    var morningTech = day == 0 && window.Start <= 10 && technicians.Count > 0 ? technicians[(i / Windows.Length) % technicians.Count] : null;
                    var home = morningTech is null ? null : PickHomeFor(customers, morningTech);
                    if (home is null)
                    {
                        // Nothing at these homes needs this technician's trade (a plumber, say).
                        morningTech = null;
                    }

                    var (customer, equipment) = home ?? PickHome(customers);
                    var property = customer.Properties[0];
                    var model = EquipmentCatalog.ByCode(ModelCode(equipment));
                    var seasonal = model.Faults.Where(f => f.Months.Contains(date.Month)).ToArray();
                    var fault = seasonal.Length > 0 ? PickWeighted(seasonal.Select(f => (f, f.Weight)).ToArray()) : null;

                    var job = new Job
                    {
                        Number = "",
                        Branch = branch,
                        Customer = customer,
                        Property = property,
                        Equipment = equipment,
                        Type = fault is null ? JobType.Maintenance : JobType.Repair,
                        Priority = fault is null ? JobPriority.Normal : PickWeighted((JobPriority.Urgent, 2), (JobPriority.Normal, 3)),
                        RequiredSkill = model.Skill,
                        RequiredCertification = model.Certification,
                        WindowStart = AtLocal(date, window.Start),
                        WindowEnd = AtLocal(date, window.End),
                        EstimatedMinutes = fault?.Minutes ?? 60,
                        Summary = fault?.Symptom ?? $"Seasonal tune-up: {Describe(equipment.Kind)}.",
                        CreatedAt = AtLocal(date.AddDays(-_random.Next(1, 10)), 10),
                        Status = JobStatus.Scheduled,
                    };

                    // Today is frozen at late morning; later days are partly assigned already. Only a
                    // technician free in this window is considered.
                    var technician = morningTech is not null && !_booked.Contains((morningTech, date, window.Start))
                        ? morningTech
                        : FreeTechnician(branch, model.Certification, model.Skill, date, window.Start);
                    if (day == 0)
                    {
                        PlaceOnToday(job, window.Start, technician, fault, i);
                    }
                    else if (technician is not null && _random.NextDouble() < 0.5)
                    {
                        job.Technician = technician;
                        job.Status = JobStatus.Dispatched;
                        job.DispatchedAt = AtLocal(_today, 8);
                    }

                    if (job.Technician is { } assigned)
                    {
                        _booked.Add((assigned, date, window.Start));
                    }

                    _data.Jobs.Add(job);
                }
            }
        }
    }

    private (Customer Customer, Equipment Equipment) PickHome(IReadOnlyList<Customer> customers)
    {
        var customer = Pick(customers);
        return (customer, Pick(customer.Properties[0].Equipment));
    }

    /// <summary>A home with equipment this technician is licensed and skilled to work on.</summary>
    private (Customer Customer, Equipment Equipment)? PickHomeFor(IReadOnlyList<Customer> customers, Technician technician)
    {
        var fits = customers
            .SelectMany(c => c.Properties[0].Equipment.Select(e => (Customer: c, Equipment: e)))
            .Where(x =>
            {
                var model = EquipmentCatalog.ByCode(ModelCode(x.Equipment));
                return (technician.Certifications & model.Certification) == model.Certification && (technician.Skills & model.Skill) != 0;
            })
            .ToList();
        return fits.Count > 0 ? Pick(fits) : null;
    }

    /// <summary>
    /// A qualified technician from this branch with nothing in this window. No borrowing from other
    /// branches on the live days: borrowing first-come-first-served let one branch's morning use up
    /// another's only gas technician. A job nobody local can take waits for the dispatcher.
    /// </summary>
    private Technician? FreeTechnician(Branch branch, Certifications certification, TechnicianSkills skill, DateOnly day, int hour)
    {
        var free = _data.Technicians
            .Where(t => t.Branch == branch && (t.Certifications & certification) == certification && (t.Skills & skill) != 0 && !_booked.Contains((t, day, hour)))
            .ToList();
        return free.Count > 0 ? Pick(free) : null;
    }

    private void PlaceOnToday(Job job, int windowStart, Technician? technician, Fault? fault, int index)
    {
        if (technician is null)
        {
            return;
        }

        switch (windowStart)
        {
            case 8:
                job.Technician = technician;
                job.Status = JobStatus.Completed;
                job.DispatchedAt = AtLocal(_today, 7);
                job.CompletedAt = job.WindowStart.AddMinutes(job.EstimatedMinutes + 20);
                job.Resolution = fault?.Resolution ?? "Seasonal maintenance per checklist.";
                job.InvoiceTotal = fault is null ? 0m : Money(fault.Low, fault.High);
                job.FirstTimeFix = true;
                break;
            case 10:
                job.Technician = technician;
                job.Status = JobStatus.InProgress;
                job.DispatchedAt = AtLocal(_today, 7);
                break;
            default:
                // Afternoon: alternately assigned and waiting for the dispatcher (and a suggestion).
                // By position, not by chance, so every branch's board always has both.
                if (index % 8 >= 4)
                {
                    job.Technician = technician;
                    job.Status = JobStatus.Dispatched;
                    job.DispatchedAt = AtLocal(_today, 7);
                }

                break;
        }
    }

    /// <summary>
    /// The demo's centrepiece: Priya Raman's CAL-80 ignitor has failed a third time. It's an
    /// unassigned emergency in Guelph, where the closest technician only holds a G3 and the
    /// AC specialist holds no gas licence, so the suggestion has to explain why it picked Nadia.
    /// </summary>
    private void AddTheDemoEmergency()
    {
        var priya = _data.AnchorCustomers.Single(c => c.LastName == "Raman");
        var property = priya.Properties[0];
        var furnace = property.Equipment.Single(e => e.Kind == EquipmentKind.Furnace);

        _data.Jobs.Add(new Job
        {
            Number = "",
            Branch = priya.Branch,
            Customer = priya,
            Property = property,
            Equipment = furnace,
            Type = JobType.Repair,
            Priority = JobPriority.Emergency,
            Status = JobStatus.Scheduled,
            RequiredSkill = TechnicianSkills.Furnaces,
            RequiredCertification = Certifications.GasG2,
            WindowStart = AtLocal(_today, 12),
            WindowEnd = AtLocal(_today, 14),
            EstimatedMinutes = 75,
            Summary = "No heat. Furnace blower runs but it won't light. Third time this has happened.",
            CreatedAt = AtLocal(_today, 7).AddMinutes(42),
        });
    }

    private void NumberJobs()
    {
        // Numbered in the order they were booked, the way a real system hands them out.
        var number = 21_000;
        foreach (var job in _data.Jobs.OrderBy(j => j.CreatedAt).ThenBy(j => j.WindowStart))
        {
            job.Number = $"J-{++number}";
        }
    }

    // ---------------------------------------------------------------- helpers

    private Technician? QualifiedTechnician(Branch branch, Certifications certification, TechnicianSkills skill)
    {
        bool Qualified(Technician t) => (t.Certifications & certification) == certification && (t.Skills & skill) != 0;

        var local = _data.Technicians.Where(t => t.Branch == branch && Qualified(t)).ToList();
        if (local.Count > 0)
        {
            return Pick(local);
        }

        // Nobody local can do it: borrow from another branch, as small shops really do.
        var anyone = _data.Technicians.Where(Qualified).ToList();
        return anyone.Count > 0 ? Pick(anyone) : null;
    }

    private static string ModelCode(Equipment equipment) =>
        EquipmentCatalog.Models.First(m => equipment.Model.StartsWith(m.Code, StringComparison.Ordinal)).Code;

    private static string Describe(EquipmentKind kind) => kind switch
    {
        EquipmentKind.AirConditioner => "air conditioner",
        EquipmentKind.HeatPump => "heat pump",
        EquipmentKind.WaterHeater => "water heater",
        EquipmentKind.TanklessWaterHeater => "tankless water heater",
        EquipmentKind.AirExchanger => "air exchanger",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static DateTimeOffset AtLocal(DateOnly day, int hour)
    {
        var local = day.ToDateTime(new TimeOnly(hour, 0));
        return new DateTimeOffset(local, LocalZone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static DateOnly Later(DateOnly a, DateOnly b) => a > b ? a : b;

    private DateOnly RandomDateBetween(DateOnly from, DateOnly to) =>
        to <= from ? from : from.AddDays(_random.Next(to.DayNumber - from.DayNumber));

    private DateOnly? RandomDayInMonths(DateOnly from, DateOnly to, int[] months)
    {
        var candidates = new List<DateOnly>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (months.Contains(day.Month) && day.DayOfWeek != DayOfWeek.Sunday)
            {
                candidates.Add(day);
            }
        }

        return candidates.Count == 0 ? null : candidates[_random.Next(candidates.Count)];
    }

    private decimal Money(decimal low, decimal high)
    {
        var value = low + (decimal)_random.NextDouble() * (high - low);
        return Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2;
    }

    private string RandomPostalCode()
    {
        const string letters = "ABCEGHJKLMNPRSTVWXYZ";
        char L() => letters[_random.Next(letters.Length)];
        return $"N{_random.Next(1, 9)}{L()} {_random.Next(0, 9)}{L()}{_random.Next(0, 9)}";
    }

    /// <summary>Records an address as taken. Anchors claim theirs first, before any are generated.</summary>
    private string Claim(string street, string town)
    {
        if (!_addresses.Add($"{street}|{town}"))
        {
            throw new InvalidOperationException($"Two anchor customers share {street}, {town}.");
        }

        return street;
    }

    private string UniqueStreet(string town)
    {
        while (true)
        {
            var street = $"{_random.Next(1, 400)} {Pick(PlaceAndPeopleCatalog.StreetNames)}";
            if (_addresses.Add($"{street}|{town}"))
            {
                return street;
            }
        }
    }

    private string PickSize(EquipmentModel model) => Sizes.TryGetValue(model.Code, out var sizes) ? Pick(sizes) : "";

    private T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

    private T PickWeighted<T>(params (T Item, int Weight)[] options)
    {
        var roll = _random.Next(options.Sum(o => o.Weight));
        foreach (var (item, weight) in options)
        {
            if (roll < weight)
            {
                return item;
            }

            roll -= weight;
        }

        return options[^1].Item;
    }

    private Queue<string> BuildPhonePool()
    {
        var reserved = AnchorCustomers.All.Select(a => a.Phone)
            .Concat(PlaceAndPeopleCatalog.Branches.Select(b => b.Phone))
            .Concat(SeedCalls.CallerPhones())
            .ToHashSet(StringComparer.Ordinal);

        var numbers = PlaceAndPeopleCatalog.AreaCodes
            .SelectMany(area => Enumerable.Range(100, 100).Select(n => $"{area}-555-0{n}"))
            .Where(n => !reserved.Contains(n))
            .ToArray();

        _random.Shuffle(numbers);
        return new Queue<string>(numbers);
    }
}

/// <summary>Everything the generator produces, ready to add to a context in one go.</summary>
public sealed class DemoData
{
    public List<Branch> Branches { get; } = [];
    public List<Technician> Technicians { get; } = [];
    public List<Customer> Customers { get; } = [];
    public List<Job> Jobs { get; } = [];
    public List<KnowledgeDocument> Documents { get; } = [];
    public List<CallRecord> Calls { get; } = [];

    /// <summary>The hand-written customers the demo and evals rely on. A subset of <see cref="Customers"/>.</summary>
    public List<Customer> AnchorCustomers { get; } = [];
}
