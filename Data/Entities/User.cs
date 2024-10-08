﻿using Data.Enums;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;

namespace Data.Entities;

public class User : IdentityUser<Guid>
{
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Avatar { get; set; }
    public Gender? Gender { get; set; }
    public bool IsPublicGender { get; set; } = false;
    public DateOnly? Dob { get; set; }
    public float? Star { get; set; } = 5;
    public long? Profit { get; set; }
    public bool IsDeleted { get; set; }
    public float Priority { get; set; } = 2;
    public bool IsActive { get; set; } = true;
    public DateTime DateCreated { get; set; } = DateTime.Now;
    public DateTime DateUpdated { get; set; } = DateTime.Now;
    public DateTime? LastTripTime { get; set; }
    public int TotalRequest { get; set; } = 0;
    public int DeclineRequest { get; set; } = 0;
    public virtual ICollection<UserRole> UserRoles { get; set; }

    //Driver
    public virtual ICollection<DriverLocation> DriverLocations { get; set; }
    public virtual ICollection<DriverStatus> DriverStatuses { get; set; }
    public virtual ICollection<Support> Supports { get; set; }

}
