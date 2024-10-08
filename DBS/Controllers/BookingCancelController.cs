﻿using Data.Common.PaginationModel;
using Data.Enums;
using Data.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Core;
using Services.Utils;

namespace DBS.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(AuthenticationSchemes = "Bearer")]
public class BookingCancelController : ControllerBase
{
    private readonly IBookingCancelService _bookingCancelService;

    public BookingCancelController(IBookingCancelService bookingCancelService)
    {
        _bookingCancelService = bookingCancelService;
    }

    [HttpPost("Customer")]
    public async Task<ActionResult> CustomerCancel([FromForm] BookingCancelCreateModel model)
    {
        var result = await _bookingCancelService.CustomerCancel(model, Guid.Parse(User.GetId()));
        if (result.Succeed) return Ok(result.Data);
        return BadRequest(result.ErrorMessage);
    }

    [HttpPost("Driver")]
    public async Task<ActionResult> DriverCancel([FromForm] BookingCancelCreateModel model)
    {
        var result = await _bookingCancelService.DriverCancel(model, Guid.Parse(User.GetId()));
        if (result.Succeed) return Ok(result.Data);
        return BadRequest(result.ErrorMessage);
    }

    [HttpGet]
    public async Task<ActionResult> Get([FromQuery] PagingParam<SortCriteria> paginationModel)
    {
        var result = await _bookingCancelService.Get(paginationModel, Guid.Parse(User.GetId()));
        if (result.Succeed) return Ok(result.Data);
        return BadRequest(result.ErrorMessage);
    }

    [HttpGet("{BookingId}")]
    public async Task<ActionResult> GetByBookingId(Guid BookingId)
    {
        var result = await _bookingCancelService.GetByBookingID(BookingId, Guid.Parse(User.GetId()));
        if (result.Succeed) return Ok(result.Data);
        return BadRequest(result.ErrorMessage);
    }

}
