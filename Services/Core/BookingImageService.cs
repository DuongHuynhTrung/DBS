﻿
using AutoMapper;
using Confluent.Kafka;
using Data.DataAccess;
using Data.Entities;
using Data.Enums;
using Data.Model;
using Data.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.Utils;

namespace Services.Core;

public interface IBookingImageService
{
    Task<ResultModel> AddImageCheckIn(BookingImageCreateModel model);
    Task<ResultModel> AddImageCheckOut(BookingImageCreateModel model);
    Task<ResultModel> GetCheckInImagesByBookingId(Guid BookingId);
    Task<ResultModel> GetCheckOutImagesByBookingId(Guid BookingId);
    Task<ResultModel> UpdateImage(BookingImageUpdateModel model, Guid BookingImageId);
    Task<ResultModel> DeleteImage(Guid BookingImageId);
    Task<ResultModel> DownloadImage(FileModel model);
}

public class BookingImageService : IBookingImageService
{
    private readonly AppDbContext _dbContext;
    private readonly IMapper _mapper;
    private readonly IMailService _mailService;
    private readonly IConfiguration _configuration;
    private readonly IProducer<Null, string> _producer;
    private readonly UserManager<User> _userManager;

    public BookingImageService(AppDbContext dbContext, IMapper mapper, IMailService mailService, IConfiguration configuration, IProducer<Null, string> producer, UserManager<User> userManager)
    {
        _dbContext = dbContext;
        _mapper = mapper;
        _mailService = mailService;
        _configuration = configuration;
        _producer = producer;
        _userManager = userManager;
    }

    public async Task<ResultModel> AddImageCheckIn(BookingImageCreateModel model)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var booking = _dbContext.Bookings.Where(_ => _.Id == model.BookingId && !_.IsDeleted).FirstOrDefault();
            if (booking == null)
            {
                result.ErrorMessage = "Booking not exist!";
                return result;
            }
            if (booking.Status != BookingStatus.CheckIn)
            {
                result.ErrorMessage = "Driver not CheckIn";
                return result;
            }
            var checkExist = _dbContext.BookingImages
                .Where(_ => _.BookingId == booking.Id && _.BookingImageTime == BookingImageTime.CheckIn && _.BookingImageType == model.BookingImageType && !_.IsDeleted).FirstOrDefault();
            if (checkExist != null)
            {
                result.ErrorMessage = $"Booking Image when CheckIn with type {checkExist.BookingImageType} existed";
                return result;
            }
            var bookingImage = _mapper.Map<BookingImageCreateModel, BookingImage>(model);
            _dbContext.BookingImages.Add(bookingImage);
            bookingImage.BookingImageTime = BookingImageTime.CheckIn;
            string dirPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "BookingImageCheckIn", bookingImage.Id.ToString());
            bookingImage.ImageUrl = await MyFunction.UploadImageAsync(model.File, dirPath);
            await _dbContext.SaveChangesAsync();

            result.Succeed = true;
            result.Data = _mapper.Map<BookingImageModel>(bookingImage);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> AddImageCheckOut(BookingImageCreateModel model)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var booking = _dbContext.Bookings.Where(_ => _.Id == model.BookingId && !_.IsDeleted).FirstOrDefault();
            if (booking == null)
            {
                result.ErrorMessage = "Booking not exist!";
                return result;
            }
            if (booking.Status != BookingStatus.CheckOut)
            {
                result.ErrorMessage = "Driver not CheckOut";
                return result;
            }
            var checkExist = _dbContext.BookingImages
                .Where(_ => _.BookingId == booking.Id && _.BookingImageTime == BookingImageTime.CheckOut && _.BookingImageType == model.BookingImageType && !_.IsDeleted).FirstOrDefault();
            if (checkExist != null)
            {
                result.ErrorMessage = $"Booking Image when CheckOut with type {checkExist.BookingImageType} existed";
                return result;
            }
            var bookingImage = _mapper.Map<BookingImageCreateModel, BookingImage>(model);
            _dbContext.BookingImages.Add(bookingImage);
            bookingImage.BookingImageTime = BookingImageTime.CheckOut;
            string dirPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "BookingImageCheckIn", bookingImage.Id.ToString());
            bookingImage.ImageUrl = await MyFunction.UploadImageAsync(model.File, dirPath);
            await _dbContext.SaveChangesAsync();

            result.Succeed = true;
            result.Data = _mapper.Map<BookingImageModel>(bookingImage);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> DeleteImage(Guid BookingImageId)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var bookingImage = _dbContext.BookingImages.Where(_ => _.Id == BookingImageId && !_.IsDeleted).FirstOrDefault();
            if (bookingImage == null)
            {
                result.ErrorMessage = "Booking Image not exist!";
                return result;
            }
            string dirPathDelete = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            MyFunction.DeleteFile(dirPathDelete + bookingImage.ImageUrl);

            _dbContext.BookingImages.Remove(bookingImage);
            await _dbContext.SaveChangesAsync();

            result.Succeed = true;
            result.Data = "Delete Booking Image successful";
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> DownloadImage(FileModel model)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var bookingImage = _dbContext.BookingImages.Where(_ => _.Id == model.Id && !_.IsDeleted).FirstOrDefault();
            if (bookingImage == null)
            {
                result.Succeed = false;
                result.ErrorMessage = "Booking Image not found";
            }
            else
            {
                string dirPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                if (bookingImage.ImageUrl == null || !bookingImage.ImageUrl.Contains(model.Path))
                {
                    result.ErrorMessage = "Image does not exist";
                    result.Succeed = false;
                    return result;
                }
                result.Data = await MyFunction.DownloadFile(dirPath + model.Path);
                result.Succeed = true;
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> GetCheckInImagesByBookingId(Guid BookingId)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var bookingImages = _dbContext.BookingImages
                .Include(_ => _.Booking)
                .Where(_ => _.BookingId == BookingId && _.BookingImageTime == BookingImageTime.CheckIn && !_.IsDeleted)
                .ToList();
            if (bookingImages == null || bookingImages.Count == 0)
            {
                result.ErrorMessage = "Booking Image not exist!";
                return result;
            }
            var data = _mapper.Map<List<BookingImageModel>>(bookingImages);

            result.Data = data;
            result.Succeed = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> GetCheckOutImagesByBookingId(Guid BookingId)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var bookingImages = _dbContext.BookingImages
                .Include(_ => _.Booking)
                .Where(_ => _.BookingId == BookingId && _.BookingImageTime == BookingImageTime.CheckOut && !_.IsDeleted)
                .ToList();
            if (bookingImages == null || bookingImages.Count == 0)
            {
                result.ErrorMessage = "Booking Image not exist!";
                return result;
            }
            var data = _mapper.Map<List<BookingImageModel>>(bookingImages);

            result.Data = data;
            result.Succeed = true;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }

    public async Task<ResultModel> UpdateImage(BookingImageUpdateModel model, Guid BookingImageId)
    {
        var result = new ResultModel();
        result.Succeed = false;
        try
        {
            var bookingImage = _dbContext.BookingImages
                .Include(_ => _.Booking)
                .Where(_ => _.Id == BookingImageId && !_.IsDeleted).FirstOrDefault();
            if (bookingImage == null)
            {
                result.ErrorMessage = "Booking Image not exist!";
                return result;
            }
            if (model.File != null)
            {
                string dirPathDelete = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                MyFunction.DeleteFile(dirPathDelete + bookingImage.ImageUrl);
                string dirPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "BookingImage", bookingImage.Id.ToString());
                bookingImage.ImageUrl = await MyFunction.UploadImageAsync(model.File, dirPath);
            }
            bookingImage.DateUpdated = DateTime.Now;
            await _dbContext.SaveChangesAsync();

            var data = _mapper.Map<BookingImageModel>(bookingImage);

            result.Succeed = true;
            result.Data = data;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        }
        return result;
    }
}