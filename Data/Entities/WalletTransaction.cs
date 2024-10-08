﻿
using Data.Enums;
using Data.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Data.Entities;

public class WalletTransaction : BaseEntity
{
    public Guid WalletId { get; set; }
    [ForeignKey("WalletId")]
    public virtual Wallet Wallet { get; set; }
    public long TotalMoney { get; set; }
    public TypeWalletTransaction TypeWalletTransaction { get; set; }
    public PaymentType? PaymentType { get; set; }
    public WalletTransactionStatus Status { get; set; } = WalletTransactionStatus.Waiting;
    public Guid? LinkedAccountId { get; set; }
    [ForeignKey("LinkedAccountId")]
    public virtual LinkedAccount? LinkedAccount { get; set; }
}
