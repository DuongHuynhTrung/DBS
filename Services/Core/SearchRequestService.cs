﻿using AutoMapper;
using Confluent.Kafka;
using Data.Common.PaginationModel;
using Data.DataAccess;
using Data.Entities;
using Data.Enums;
using Data.Model;
using Data.Models;
using Data.Utils;
using Data.Utils.Paging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Services.Core;

public interface ISearchRequestService
{
    Task<ResultModel> Add(SearchRequestCreateModel model, Guid customerId);
    Task<ResultModel> GetOfCustomer(PagingParam<SortCriteria> paginationModel, Guid customerId);
    Task<ResultModel> UpdateStatusToComplete(Guid SearchRequestId, Guid customerId);
    Task<ResultModel> UpdateStatusToCancel(Guid SearchRequestId, Guid customerId, Guid DriverId);
    Task<ResultModel> UpdateStatusToCancel(Guid SearchRequestId, Guid customerId);
    Task<ResultModel> DriverMissSearchRequest(Guid customerId, Guid DriverId);
    Task<ResultModel> NewDriver(NewDriverModel model);
    Task<ResultModel> CheckExistSearchRequestProcessing(Guid customerId);
    Task<ResultModel> SendSearchRequestToDriver(Guid searchRequestId, Guid driverId);

}
public class SearchRequestService : ISearchRequestService
{
    private readonly AppDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IMailService _mailService;
    private readonly IConfiguration _configuration;
    private readonly IProducer<Null, string> _producer;
    private readonly UserManager<User> _userManager;

    public SearchRequestService(AppDbContext dbContext, IMapper mapper, IConfiguration configuration,
        UserManager<User> userManager,
        IMailService mailService, IProducer<Null, string> producer)
    {
        _dbContext = dbContext;
        _mapper = mapper;
        _configuration = configuration;
        _mailService = mailService;
        _producer = producer;
        _userManager = userManager;
    }

    public async Task<ResultModel> Add(SearchRequestCreateModel model, Guid customerId)
    {
        var result = new ResultModel { Succeed = false };
        try
        {
            var customer = await _dbContext.Users
                .FirstOrDefaultAsync(_ => _.Id == customerId && !_.IsDeleted);

            if (customer == null)
            {
                result.ErrorMessage = "User not exists";
                return result;
            }

            var checkCustomer = await _userManager.IsInRoleAsync(customer, RoleNormalizedName.Customer);
            if (!checkCustomer)
            {
                result.ErrorMessage = "The user must be a customer";
                return result;
            }

            var driver = await _dbContext.Users
                .Include(_ => _.DriverLocations)
                .FirstOrDefaultAsync(_ => _.Id == model.DriverId && !_.IsDeleted);

            if (driver == null)
            {
                result.ErrorMessage = "User not exists";
                return result;
            }

            var checkDriver = await _userManager.IsInRoleAsync(driver, RoleNormalizedName.Driver);
            if (!checkDriver)
            {
                result.ErrorMessage = "The user must be a driver";
                return result;
            }

            var searchRequest = _mapper.Map<SearchRequestCreateModel, SearchRequest>(model);
            searchRequest.CustomerId = customer.Id;

            var bookingVehicle = _mapper.Map<BookingVehicleModel, BookingVehicle>(model.BookingVehicle);
            await _dbContext.BookingVehicles.AddAsync(bookingVehicle);
            searchRequest.BookingVehicle = bookingVehicle;

            if (model.BookingType == BookingType.Someone)
            {
                if (model.CustomerBookedOnBehalf == null)
                {
                    result.ErrorMessage = "Booking for Someone requires CustomerBookedOnBehalf";
                    return result;
                }

                var customerBookedOnBehalf = _mapper.Map<CustomerBookedOnBehalfModel, CustomerBookedOnBehalf>(model.CustomerBookedOnBehalf);
                await _dbContext.CustomerBookedOnBehalves.AddAsync(customerBookedOnBehalf);
                searchRequest.CustomerBookedOnBehalf = customerBookedOnBehalf;
            }

            await _dbContext.SearchRequests.AddAsync(searchRequest);
            await _dbContext.SaveChangesAsync();

            var data = _mapper.Map<SearchRequestModel>(searchRequest);
            data.Customer = _mapper.Map<UserModel>(customer);
            data.BookingVehicle = _mapper.Map<BookingVehicleModel>(searchRequest.BookingVehicle);
            data.CustomerBookedOnBehalf = _mapper.Map<CustomerBookedOnBehalfModel>(searchRequest.CustomerBookedOnBehalf);
            data.DriverId = driver.Id;

            var kafkaModel = new KafkaModel
            {
                UserReceiveNotice = new List<Guid> { driver.Id },
                Payload = data
            };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(kafkaModel);

            await _producer.ProduceAsync("dbs-search-request-create", new Message<Null, string> { Value = json });
            _producer.Flush();

            result.Succeed = true;
            result.Data = searchRequest.Id;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException?.Message ?? ex.Message;
        }
        return result;
    }


    public async Task<ResultModel> GetOfCustomer(PagingParam<SortCriteria> paginationModel, Guid customerId)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var customer = _dbContext.Users.Where(_ => _.Id == customerId && !_.IsDeleted).FirstOrDefault();
            if (customer == null)
            {
                result.ErrorMessage = "User not exists";
                result.Succeed = false;
                return result;
            }
            var checkCustomer = await _userManager.IsInRoleAsync(customer, RoleNormalizedName.Customer);
            if (!checkCustomer)
            {
                result.ErrorMessage = "The user must be a customer";
                result.Succeed = false;
                return result;
            }
            var data = _dbContext.SearchRequests.Where(_ => _.CustomerId == customerId && !_.IsDeleted);
            var paging = new PagingModel(paginationModel.PageIndex, paginationModel.PageSize, data.Count());
            var searchRequests = data.GetWithSorting(paginationModel.SortKey.ToString(), paginationModel.SortOrder);
            searchRequests = searchRequests.GetWithPaging(paginationModel.PageIndex, paginationModel.PageSize);
            var viewModels = _mapper.ProjectTo<SearchRequestModel>(searchRequests);
            paging.Data = viewModels;
            result.Data = paging;
            result.Succeed = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> UpdateStatusToComplete(Guid SearchRequestId, Guid customerId)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var customer = _dbContext.Users.Where(_ => _.Id == customerId && !_.IsDeleted).FirstOrDefault();
            if (customer == null)
            {
                result.ErrorMessage = "User not exist";
                return result;
            }
            var checkCustomer = await _userManager.IsInRoleAsync(customer, RoleNormalizedName.Customer);
            if (!checkCustomer)
            {
                result.ErrorMessage = "The user must be a customer";
                return result;
            }
            var data = _dbContext.SearchRequests
                .Where(_ => _.CustomerId == customerId && _.Id == SearchRequestId && !_.IsDeleted).FirstOrDefault();
            if (data == null)
            {
                result.ErrorMessage = "SearchRequest not exist";
                return result;
            }
            if (data.Status != SearchRequestStatus.Processing)
            {
                result.ErrorMessage = "SearchRequest status not suitable";
                return result;
            }
            data.Status = SearchRequestStatus.Completed;
            data.DateUpdated = DateTime.Now;
            await _dbContext.SaveChangesAsync();

            result.Data = _mapper.Map<SearchRequestModel>(data);
            result.Succeed = true;
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> UpdateStatusToCancel(Guid SearchRequestId, Guid customerId, Guid DriverId)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var customer = _dbContext.Users.Where(_ => _.Id == customerId && !_.IsDeleted).FirstOrDefault();
            if (customer == null)
            {
                result.ErrorMessage = "User not exist";
                return result;
            }
            var checkCustomer = await _userManager.IsInRoleAsync(customer, RoleNormalizedName.Customer);
            if (!checkCustomer)
            {
                result.ErrorMessage = "The user must be a customer";
                return result;
            }
            var driver = _dbContext.Users.Where(_ => _.Id == DriverId && !_.IsDeleted).FirstOrDefault();
            if (driver == null)
            {
                result.ErrorMessage = "User not exist";
                return result;
            }
            var checkDriver = await _userManager.IsInRoleAsync(driver, RoleNormalizedName.Driver);
            if (!checkDriver)
            {
                result.ErrorMessage = "The user must be a driver";
                return result;
            }
            var searchRequest = _dbContext.SearchRequests
                .Where(_ => _.CustomerId == customerId && _.Id == SearchRequestId && !_.IsDeleted).FirstOrDefault();
            if (searchRequest == null)
            {
                result.ErrorMessage = "SearchRequest not exist";
                return result;
            }
            if (searchRequest.Status != SearchRequestStatus.Processing)
            {
                result.ErrorMessage = "SearchRequest status not suitable";
                return result;
            }
            searchRequest.Status = SearchRequestStatus.Cancel;
            searchRequest.DateUpdated = DateTime.Now;

            var data = _mapper.Map<SearchRequestModel>(searchRequest);
            var kafkaModel = new KafkaModel { UserReceiveNotice = new List<Guid>() { DriverId }, Payload = data };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(kafkaModel);
            await _producer.ProduceAsync("dbs-searchrequest-customer-cancel", new Message<Null, string> { Value = json });
            _producer.Flush();

            var wallet = _dbContext.Wallets.Where(_ => _.UserId == searchRequest.CustomerId).FirstOrDefault();
            if (wallet == null)
            {
                result.ErrorMessage = "Wallet not exist";
                return result;
            }

            var walletTransaction = new WalletTransaction
            {
                TotalMoney = searchRequest.Price,
                TypeWalletTransaction = TypeWalletTransaction.Refund,
                WalletId = wallet.Id,
                Status = WalletTransactionStatus.Success,
            };
            _dbContext.WalletTransactions.Add(walletTransaction);

            wallet.TotalMoney += walletTransaction.TotalMoney;
            wallet.DateUpdated = DateTime.Now;
            _dbContext.Wallets.Update(wallet);

            var payloadWallet = _mapper.Map<WalletModel>(wallet);
            var kafkaModelWallet = new KafkaModel { UserReceiveNotice = new List<Guid>() { searchRequest.CustomerId }, Payload = payloadWallet };
            var jsonWallet = Newtonsoft.Json.JsonConvert.SerializeObject(kafkaModelWallet);
            await _producer.ProduceAsync("dbs-wallet-refund-customer", new Message<Null, string> { Value = jsonWallet });
            _producer.Flush();

            await _dbContext.SaveChangesAsync();

            result.Data = data;
            result.Succeed = true;
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> UpdateStatusToCancel(Guid SearchRequestId, Guid customerId)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var customer = _dbContext.Users.Where(_ => _.Id == customerId && !_.IsDeleted).FirstOrDefault();
            if (customer == null)
            {
                result.ErrorMessage = "User not exist";
                return result;
            }
            var checkCustomer = await _userManager.IsInRoleAsync(customer, RoleNormalizedName.Customer);
            if (!checkCustomer)
            {
                result.ErrorMessage = "The user must be a customer";
                return result;
            }
            var searchRequest = _dbContext.SearchRequests
                .Where(_ => _.CustomerId == customerId && _.Id == SearchRequestId && !_.IsDeleted).FirstOrDefault();
            if (searchRequest == null)
            {
                result.ErrorMessage = "SearchRequest not exist";
                return result;
            }
            if (searchRequest.Status != SearchRequestStatus.Processing)
            {
                result.ErrorMessage = "SearchRequest status not suitable";
                return result;
            }
            searchRequest.Status = SearchRequestStatus.Cancel;
            searchRequest.DateUpdated = DateTime.Now;

            var data = _mapper.Map<SearchRequestModel>(searchRequest);


            var wallet = _dbContext.Wallets.Where(_ => _.UserId == searchRequest.CustomerId).FirstOrDefault();
            if (wallet == null)
            {
                result.ErrorMessage = "Wallet not exist";
                return result;
            }

            var walletTransaction = new WalletTransaction
            {
                TotalMoney = searchRequest.Price,
                TypeWalletTransaction = TypeWalletTransaction.Refund,
                WalletId = wallet.Id,
                Status = WalletTransactionStatus.Success,
            };
            _dbContext.WalletTransactions.Add(walletTransaction);

            wallet.TotalMoney += walletTransaction.TotalMoney;
            wallet.DateUpdated = DateTime.Now;
            _dbContext.Wallets.Update(wallet);

            var payloadWallet = _mapper.Map<WalletModel>(wallet);
            var kafkaModelWallet = new KafkaModel { UserReceiveNotice = new List<Guid>() { searchRequest.CustomerId }, Payload = payloadWallet };
            var jsonWallet = Newtonsoft.Json.JsonConvert.SerializeObject(kafkaModelWallet);
            await _producer.ProduceAsync("dbs-wallet-refund-customer", new Message<Null, string> { Value = jsonWallet });
            _producer.Flush();


            await _dbContext.SaveChangesAsync();

            result.Data = data;
            result.Succeed = true;
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> DriverMissSearchRequest(Guid customerId, Guid DriverId)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var customer = _dbContext.Users.Where(_ => _.Id == customerId && !_.IsDeleted).FirstOrDefault();
            if (customer == null)
            {
                result.ErrorMessage = "User not exist";
                return result;
            }
            var checkCustomer = await _userManager.IsInRoleAsync(customer, RoleNormalizedName.Customer);
            if (!checkCustomer)
            {
                result.ErrorMessage = "The user must be a customer";
                return result;
            }
            var driver = _dbContext.Users
                .Include(_ => _.DriverStatuses)
                .Include(_ => _.DriverLocations)
                .Where(_ => _.Id == DriverId && !_.IsDeleted).FirstOrDefault();
            if (driver == null)
            {
                result.ErrorMessage = "User not exist";
                return result;
            }
            var checkDriver = await _userManager.IsInRoleAsync(driver, RoleNormalizedName.Driver);
            if (!checkDriver)
            {
                result.ErrorMessage = "The user must be a driver";
                return result;
            }
            var driverStatus = driver.DriverStatuses.FirstOrDefault();
            driverStatus.IsOnline = false;
            driverStatus.IsFree = false;
            _dbContext.DriverStatuses.Update(driverStatus);

            if (driver.Priority > 0)
            {
                driver.Priority -= 0.1f;
            }

            if (driver.Priority == 0)
            {
                driver.IsActive = false;
                var driverBan = _mapper.Map<UserModel>(driver);
                var kafkaModelBan = new KafkaModel { UserReceiveNotice = new List<Guid>() { DriverId }, Payload = driverBan };
                var jsonBan = Newtonsoft.Json.JsonConvert.SerializeObject(kafkaModelBan);
                await _producer.ProduceAsync("dbs-driver-status-ban", new Message<Null, string> { Value = jsonBan });
                _producer.Flush();
            }
            else if (driver.Priority <= 1)
            {
                var kafkaModelWarning = new KafkaModel { UserReceiveNotice = new List<Guid>() { DriverId }, Payload = "" };
                var jsonWarning = Newtonsoft.Json.JsonConvert.SerializeObject(kafkaModelWarning);
                await _producer.ProduceAsync("dbs-driver-status-warning", new Message<Null, string> { Value = jsonWarning });
                _producer.Flush();
            }
            driver.DeclineRequest += 1;
            driver.TotalRequest += 1;
            _dbContext.Users.Update(driver);
            await _dbContext.SaveChangesAsync();

            var kafkaModelMiss = new KafkaModel { UserReceiveNotice = new List<Guid>() { customerId }, Payload = DriverId };
            var jsonMiss = Newtonsoft.Json.JsonConvert.SerializeObject(kafkaModelMiss);
            await _producer.ProduceAsync("dbs-searchrequest-driver-miss", new Message<Null, string> { Value = jsonMiss });
            _producer.Flush();

            var driverLocation = _mapper.Map<LocationModel>(driver.DriverLocations.FirstOrDefault());
            var kafkaModel = new KafkaModel { UserReceiveNotice = new List<Guid>() { DriverId }, Payload = driverLocation };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(kafkaModel);
            await _producer.ProduceAsync("dbs-driver-status-offline", new Message<Null, string> { Value = json });
            _producer.Flush();

            result.Data = _mapper.Map<UserModel>(driver);
            result.Succeed = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> NewDriver(NewDriverModel model)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var oldDriver = _dbContext.Users.Where(_ => _.Id == model.OldDriverId && !_.IsDeleted).FirstOrDefault();
            if (oldDriver == null)
            {
                result.ErrorMessage = "Old Driver not found";
                return result;
            }
            var newDriver = _dbContext.Users.Where(_ => _.Id == model.NewDriverId && !_.IsDeleted).FirstOrDefault();
            if (newDriver == null)
            {
                result.ErrorMessage = "New Driver not found";
                return result;
            }
            var searchRequest = _dbContext.SearchRequests
                .Include(_ => _.Customer)
                .Include(_ => _.BookingVehicle)
                .Include(_ => _.CustomerBookedOnBehalf)
                .Where(_ => _.Id == model.SearchRequestId && !_.IsDeleted).FirstOrDefault();
            if (searchRequest == null)
            {
                result.ErrorMessage = "SearchRequest not exist";
                return result;
            }
            if (searchRequest.Status != SearchRequestStatus.Processing)
            {
                result.ErrorMessage = "SearchRequest status not suitable";
                return result;
            }

            // Send to Old Driver
            var oldData = _mapper.Map<SearchRequestModel>(searchRequest);
            oldData.Status = SearchRequestStatus.Cancel;
            oldData.Customer = _mapper.Map<UserModel>(searchRequest.Customer);
            oldData.BookingVehicle = _mapper.Map<BookingVehicleModel>(searchRequest.BookingVehicle);
            oldData.CustomerBookedOnBehalf = _mapper.Map<CustomerBookedOnBehalfModel>(searchRequest.CustomerBookedOnBehalf);
            oldData.DriverId = oldDriver.Id;

            var oldKafkaModel = new KafkaModel { UserReceiveNotice = new List<Guid>() { model.OldDriverId }, Payload = oldData };
            var oldJson = Newtonsoft.Json.JsonConvert.SerializeObject(oldKafkaModel);
            await _producer.ProduceAsync("dbs-booking-old-driver", new Message<Null, string> { Value = oldJson });
            _producer.Flush();

            // Send to New Driver
            var data = _mapper.Map<SearchRequestModel>(searchRequest);
            data.Customer = _mapper.Map<UserModel>(searchRequest.Customer);
            data.BookingVehicle = _mapper.Map<BookingVehicleModel>(searchRequest.BookingVehicle);
            data.CustomerBookedOnBehalf = _mapper.Map<CustomerBookedOnBehalfModel>(searchRequest.CustomerBookedOnBehalf);
            data.DriverId = newDriver.Id;

            var kafkaModel = new KafkaModel { UserReceiveNotice = new List<Guid>() { model.NewDriverId }, Payload = data };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(kafkaModel);
            await _producer.ProduceAsync("dbs-booking-new-driver", new Message<Null, string> { Value = json });
            _producer.Flush();

            result.Data = data;
            result.Succeed = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> CheckExistSearchRequestProcessing(Guid customerId)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var customer = _dbContext.Users.Where(_ => _.Id == customerId && !_.IsDeleted).FirstOrDefault();
            if (customer == null)
            {
                result.ErrorMessage = "User not exist";
                return result;
            }
            var checkCustomer = await _userManager.IsInRoleAsync(customer, RoleNormalizedName.Customer);
            if (!checkCustomer)
            {
                result.ErrorMessage = "The user must be a customer";
                return result;
            }
            var searchRequest = _dbContext.SearchRequests
                .Include(_ => _.CustomerBookedOnBehalf)
                .Include(_ => _.BookingVehicle)
                .Where(_ => _.CustomerId == customerId && _.Status == SearchRequestStatus.Processing)
                .OrderByDescending(_ => _.DateCreated)
                .FirstOrDefault();

            if (searchRequest == null)
            {
                result.Succeed = true;
                result.Data = null;
                return result;
            }

            result.Data = _mapper.Map<SearchRequestModel>(searchRequest);
            result.Succeed = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> SendSearchRequestToDriver(Guid searchRequestId, Guid driverId)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var searchRequest = _dbContext.SearchRequests
               .Where(_ => _.Id == searchRequestId).FirstOrDefault();
            if (searchRequest == null)
            {
                result.ErrorMessage = "SearchRequest not exist";
                return result;
            }
            if (searchRequest.Status != SearchRequestStatus.Processing)
            {
                result.ErrorMessage = "SearchRequest status not suitable";
                return result;
            }
            var driver = _dbContext.Users
                .Where(_ => _.Id == driverId && !_.IsDeleted).FirstOrDefault();
            if (driver == null)
            {
                result.ErrorMessage = "User not exist";
                return result;
            }
            var checkDriver = await _userManager.IsInRoleAsync(driver, RoleNormalizedName.Driver);
            if (!checkDriver)
            {
                result.ErrorMessage = "The user must be a driver";
                return result;
            }

            var data = _mapper.Map<SearchRequestModel>(searchRequest);

            var kafkaModel = new KafkaModel { UserReceiveNotice = new List<Guid>() { driver.Id }, Payload = data };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(kafkaModel);
            await _producer.ProduceAsync("dbs-search-request-create", new Message<Null, string> { Value = json });
            _producer.Flush();

            result.Data = true;
            result.Succeed = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

}
